// Annuaire – Ville de Montmagny
// Recherche instantanée côté navigateur, appel en un clic, copie des numéros.
// JavaScript sans dépendance. Le DOM est construit avec createElement / textContent
// (jamais innerHTML avec des données de l'annuaire).
(() => {
    "use strict";

    const root = document.querySelector("[data-annuaire]");
    if (!root) return;

    const config = {
        directoryUrl: root.dataset.directoryUrl,
        photoUrl: root.dataset.photoUrl,
        callUrl: root.dataset.callUrl,
        // "server" : le téléphone de l'agent compose ; "link" : lien tel: ; "manual" : copie du numéro.
        callMode: root.dataset.callMode || "manual",
    };

    const els = {
        search: root.querySelector("#search"),
        clear: root.querySelector("[data-clear]"),
        service: root.querySelector("#service"),
        count: root.querySelector("[data-count]"),
        results: root.querySelector("[data-results]"),
        rail: root.querySelector("[data-rail]"),
        searchbar: root.querySelector("[data-searchbar]"),
        loadedAt: document.querySelector("[data-loaded-at]"),
        toasts: document.querySelector("[data-toasts]"),
    };

    const FIELDS = [
        { key: "ipPhone", label: "Poste", className: "ext" },
        { key: "telephoneNumber", label: "Direct" },
        { key: "mobile", label: "Mobile" },
    ];

    const ICONS = {
        search: "M10.5 3a7.5 7.5 0 0 1 5.96 12.05l4.25 4.24-1.42 1.42-4.24-4.25A7.5 7.5 0 1 1 10.5 3Zm0 2a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11Z",
        phone: "M6.6 10.8a15.1 15.1 0 0 0 6.6 6.6l2.2-2.2a1 1 0 0 1 1-.25 11.4 11.4 0 0 0 3.6.57 1 1 0 0 1 1 1V20a1 1 0 0 1-1 1A17 17 0 0 1 3 4a1 1 0 0 1 1-1h3.5a1 1 0 0 1 1 1c0 1.25.2 2.45.57 3.57a1 1 0 0 1-.25 1L6.6 10.8Z",
        copy: "M8 3h10a2 2 0 0 1 2 2v12h-2V5H8V3Zm-3 4h10a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2Zm0 2v11h10V9H5Z",
        check: "M9.5 16.2 5.3 12l-1.4 1.4 5.6 5.6 11-11-1.4-1.4-9.6 9.6Z",
        mail: "M4 5h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2Zm0 2v.4l8 5 8-5V7H4Zm16 2.75-8 5-8-5V17h16V9.75Z",
        desk: "M12 3 2 8v2h20V8L12 3Zm-7 9v6H3v2h18v-2h-2v-6h-2v6h-3v-6h-2v6H7v-6H5Z",
        info: "M12 2a10 10 0 1 1 0 20 10 10 0 0 1 0-20Zm0 2a8 8 0 1 0 0 16 8 8 0 0 0 0-16Zm-1 7h2v6h-2v-6Zm0-4h2v2h-2V7Z",
        alert: "M12 2 1 21h22L12 2Zm0 4.2L19.5 19h-15L12 6.2ZM11 10h2v5h-2v-5Zm0 6h2v2h-2v-2Z",
    };

    const state = {
        entries: [],
        cards: new Map(),
        query: "",
        service: "",
        contactsTitle: "Accueil du public",
    };

    // ---------------------------------------------------------------- Utilitaires

    function icon(name) {
        const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("aria-hidden", "true");
        const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
        path.setAttribute("d", ICONS[name]);
        path.setAttribute("fill", "currentColor");
        svg.appendChild(path);
        return svg;
    }

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = text;
        return node;
    }

    // Même normalisation que TextNormalizer côté serveur.
    function normalize(value) {
        return value
            .normalize("NFD")
            .replace(/[̀-ͯ]/g, "")
            .toLowerCase()
            .replace(/œ/g, "oe")
            .replace(/æ/g, "ae")
            .replace(/[-–—'’.,/()]/g, " ")
            .trim();
    }

    function tokenize(query) {
        const raw = query.trim();
        if (!raw) return [];
        // Saisie d'un numéro (« 01 34 28 42 10 », « +33 1 34… ») : on compare les chiffres seuls.
        const compact = raw.replace(/[\s.\-()]/g, "");
        if (/^\+?\d{2,}$/.test(compact)) {
            let digits = compact.replace("+", "");
            if (digits.startsWith("33") && digits.length === 11) digits = "0" + digits.slice(2);
            return [digits];
        }
        return normalize(raw).split(/\s+/).filter(Boolean);
    }

    function firstLetter(entry) {
        const source = normalize(entry.surname || entry.displayName || "#");
        const letter = source.charAt(0).toUpperCase();
        return /[A-Z]/.test(letter) ? letter : "#";
    }

    function initials(entry) {
        const given = (entry.givenName || "").trim();
        const surname = (entry.surname || "").trim();
        if (given || surname) return (given.charAt(0) + surname.charAt(0)).toUpperCase();
        return entry.displayName.trim().charAt(0).toUpperCase();
    }

    function plural(count, one, many) {
        return `${count.toLocaleString("fr-FR")} ${count > 1 ? many : one}`;
    }

    // ---------------------------------------------------------------- Notifications

    function toast(message, kind) {
        const node = el("div", `toast is-${kind}`);
        node.setAttribute("role", kind === "error" ? "alert" : "status");
        node.appendChild(icon(kind === "error" ? "alert" : kind === "info" ? "info" : "check"));
        node.appendChild(el("span", null, message));
        els.toasts.appendChild(node);
        setTimeout(() => node.remove(), kind === "success" ? 4500 : 7000);
    }

    // ---------------------------------------------------------------- Actions

    async function writeClipboard(text) {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return;
        }
        const area = el("textarea");
        area.value = text;
        area.setAttribute("readonly", "");
        area.style.position = "fixed";
        area.style.opacity = "0";
        document.body.appendChild(area);
        area.select();
        const ok = document.execCommand("copy");
        area.remove();
        if (!ok) throw new Error("copie refusée");
    }

    async function copy(text, button) {
        try {
            await writeClipboard(text);
            button.classList.add("is-done");
            button.replaceChildren(icon("check"));
            setTimeout(() => {
                button.classList.remove("is-done");
                button.replaceChildren(icon("copy"));
            }, 1600);
        } catch {
            toast("Impossible de copier automatiquement. Sélectionnez le numéro à la main.", "error");
        }
    }

    function antiforgeryToken() {
        const input = root.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : "";
    }

    // Le navigateur n'envoie que l'identifiant de la fiche et le champ choisi :
    // le serveur relit le numéro dans l'annuaire et ne fait composer que le poste de l'utilisateur connecté.
    // Agent sans téléphone pilotable : le numéro est copié pour être composé à la main.
    async function manualCall(entry, field) {
        const number = entry[field];
        try {
            await writeClipboard(number);
            toast(`Numéro copié : ${number}. Composez-le sur votre téléphone pour joindre ${entry.displayName}.`, "info");
        } catch {
            toast(`Composez le ${number} sur votre téléphone pour joindre ${entry.displayName}.`, "info");
        }
    }

    async function call(entry, field, button) {
        if (config.callMode === "manual") {
            await manualCall(entry, field);
            return;
        }
        if (button.getAttribute("aria-busy") === "true") return;
        button.setAttribute("aria-busy", "true");
        try {
            const response = await fetch(config.callUrl, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json",
                    "RequestVerificationToken": antiforgeryToken(),
                },
                body: JSON.stringify({ entryId: entry.id, field }),
            });
            let result = null;
            try { result = await response.json(); } catch { /* réponse non JSON */ }

            if (response.ok && result && result.success) {
                if (result.clientAction) window.location.href = result.clientAction;
                toast(result.message || "Appel lancé sur votre poste.", "success");
            } else if (response.status === 409) {
                await manualCall(entry, field);
            } else if (response.status === 429) {
                toast((result && result.message) || "Trop d'appels en peu de temps. Patientez une minute.", "error");
            } else {
                toast((result && result.message) || "L'appel n'a pas pu être lancé. Réessayez ou composez le numéro.", "error");
            }
        } catch {
            toast("Le serveur ne répond pas. Vérifiez votre connexion puis réessayez.", "error");
        } finally {
            button.removeAttribute("aria-busy");
        }
    }

    // ---------------------------------------------------------------- Construction des fiches

    function buildAvatar(entry) {
        const avatar = el("div", "avatar");
        avatar.setAttribute("aria-hidden", "true");
        if (entry.kind === "contact") {
            avatar.appendChild(icon("desk"));
        } else if (entry.hasPhoto) {
            const img = el("img");
            img.alt = "";
            img.loading = "lazy";
            img.decoding = "async";
            img.src = config.photoUrl + encodeURIComponent(entry.id);
            img.addEventListener("error", () => { avatar.replaceChildren(initials(entry)); }, { once: true });
            avatar.appendChild(img);
        } else {
            avatar.textContent = initials(entry);
        }
        return avatar;
    }

    function buildLine(entry, field) {
        const number = entry[field.key];
        const line = el("div", "line");
        line.appendChild(el("dt", null, field.label));
        line.appendChild(el("dd", field.className || null, number));

        const acts = el("div", "acts");
        const callButton = el("button", "btn-call");
        callButton.type = "button";
        callButton.appendChild(icon("phone"));
        callButton.appendChild(el("span", null, "Appeler"));
        callButton.setAttribute("aria-label", `Appeler ${entry.displayName} – ${field.label.toLowerCase()} ${number}`);
        callButton.addEventListener("click", () => call(entry, field.key, callButton));
        if (config.callMode === "manual") {
            callButton.title = "Copie le numéro, à composer sur votre téléphone";
        }

        const copyButton = el("button", "btn-copy");
        copyButton.type = "button";
        copyButton.title = "Copier le numéro";
        copyButton.setAttribute("aria-label", `Copier le numéro ${number}`);
        copyButton.appendChild(icon("copy"));
        copyButton.addEventListener("click", () => copy(number, copyButton));

        acts.append(callButton, copyButton);
        line.appendChild(acts);
        return line;
    }

    function buildCard(entry) {
        const card = el("article", "card");
        const head = el("div", "card-head");
        head.appendChild(buildAvatar(entry));

        const ident = el("div", "ident");
        const name = el("h3", "name", entry.displayName);
        ident.appendChild(name);
        if (entry.title) ident.appendChild(el("p", "role", entry.title));
        const meta = [entry.department, entry.office].filter(Boolean).join(" · ");
        if (meta) ident.appendChild(el("p", "meta", meta));
        head.appendChild(ident);
        card.appendChild(head);

        const present = FIELDS.filter((f) => entry[f.key]);
        if (present.length) {
            const lines = el("dl", "lines");
            present.forEach((f) => lines.appendChild(buildLine(entry, f)));
            card.appendChild(lines);
        }

        if (entry.mail) {
            const mail = el("a", "mail");
            mail.href = "mailto:" + entry.mail;
            mail.appendChild(icon("mail"));
            mail.appendChild(el("span", null, entry.mail));
            card.appendChild(mail);
        }
        return card;
    }

    function cardFor(entry) {
        let card = state.cards.get(entry.id);
        if (!card) {
            card = buildCard(entry);
            state.cards.set(entry.id, card);
        }
        return card;
    }

    // ---------------------------------------------------------------- Rendu

    function matches(entry, tokens) {
        if (state.service && entry.department !== state.service) return false;
        return tokens.every((t) => entry.search.includes(t));
    }

    function render() {
        const tokens = tokenize(state.query);
        const found = state.entries.filter((e) => matches(e, tokens));
        const contacts = found.filter((e) => e.kind === "contact");
        const people = found.filter((e) => e.kind !== "contact");

        const filtering = tokens.length > 0 || state.service;
        els.count.textContent = filtering
            ? plural(found.length, "résultat", "résultats")
            : plural(found.length, "fiche", "fiches");

        const fragment = document.createDocumentFragment();

        if (!found.length) {
            const empty = el("div", "empty");
            empty.appendChild(el("p", "empty-title", "Aucun résultat"));
            empty.appendChild(el("p", null,
                state.query
                    ? `Personne ne correspond à « ${state.query.trim()} »${state.service ? " dans ce service" : ""}. Essayez un nom, un service ou un numéro de poste.`
                    : "Aucune fiche dans ce service."));
            const reset = el("button", null, "Effacer la recherche");
            reset.type = "button";
            reset.addEventListener("click", resetFilters);
            empty.appendChild(reset);
            fragment.appendChild(empty);
        }

        if (contacts.length) {
            const section = el("section", "section");
            section.setAttribute("aria-label", state.contactsTitle);
            section.appendChild(el("h2", "section-title", state.contactsTitle));
            const grid = el("div", "grid");
            contacts.forEach((e) => grid.appendChild(cardFor(e)));
            section.appendChild(grid);
            fragment.appendChild(section);
        }

        const letters = [];
        if (people.length) {
            const section = el("section", "section");
            section.setAttribute("aria-label", "Agents");
            section.appendChild(el("h2", "section-title", "Agents"));
            let group = null;
            let grid = null;
            let current = null;
            people.forEach((e) => {
                const letter = firstLetter(e);
                if (letter !== current) {
                    current = letter;
                    letters.push(letter);
                    group = el("div", "letter-group");
                    group.id = "lettre-" + letter;
                    group.appendChild(el("p", "letter", letter));
                    grid = el("div", "grid");
                    group.appendChild(grid);
                    section.appendChild(group);
                }
                grid.appendChild(cardFor(e));
            });
            fragment.appendChild(section);
        }

        els.results.replaceChildren(fragment);
        els.results.setAttribute("aria-busy", "false");
        renderRail(letters);
    }

    function renderRail(letters) {
        if (!els.rail) return;
        const available = new Set(letters);
        const fragment = document.createDocumentFragment();
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ".split("").forEach((letter) => {
            if (available.has(letter)) {
                const link = el("a", null, letter);
                link.href = "#lettre-" + letter;
                fragment.appendChild(link);
            } else {
                fragment.appendChild(el("span", null, letter));
            }
        });
        els.rail.replaceChildren(fragment);
    }

    function renderServices(departments) {
        const fragment = document.createDocumentFragment();
        const all = el("option", null, "Tous les services");
        all.value = "";
        fragment.appendChild(all);
        departments.forEach((d) => {
            const option = el("option", null, d);
            option.value = d;
            fragment.appendChild(option);
        });
        els.service.replaceChildren(fragment);
        els.service.value = departments.includes(state.service) ? state.service : "";
        state.service = els.service.value;
        syncServiceStyle();
    }

    function syncServiceStyle() {
        els.service.classList.toggle("is-active", !!state.service);
    }

    // ---------------------------------------------------------------- État dans l'URL (liens partageables)

    function readUrl() {
        const params = new URLSearchParams(window.location.search);
        state.query = params.get("q") || "";
        state.service = params.get("service") || "";
        els.search.value = state.query;
    }

    function writeUrl() {
        const params = new URLSearchParams();
        if (state.query.trim()) params.set("q", state.query.trim());
        if (state.service) params.set("service", state.service);
        const qs = params.toString();
        history.replaceState(null, "", qs ? "?" + qs : window.location.pathname);
    }

    function resetFilters() {
        state.query = "";
        state.service = "";
        els.search.value = "";
        els.service.value = "";
        syncServiceStyle();
        updateClear();
        writeUrl();
        render();
        els.search.focus();
    }

    function updateClear() {
        els.clear.hidden = !els.search.value;
    }

    // ---------------------------------------------------------------- Chargement

    function showSkeleton() {
        const grid = el("div", "grid");
        for (let i = 0; i < 6; i++) grid.appendChild(el("div", "skeleton"));
        const section = el("section", "section");
        section.appendChild(grid);
        els.results.replaceChildren(section);
        els.results.setAttribute("aria-busy", "true");
    }

    function showLoadError(message) {
        const box = el("div", "empty");
        box.setAttribute("role", "alert");
        box.appendChild(el("p", "empty-title", "Annuaire indisponible"));
        box.appendChild(el("p", null, message));
        const retry = el("button", null, "Réessayer");
        retry.type = "button";
        retry.addEventListener("click", load);
        box.appendChild(retry);
        els.results.replaceChildren(box);
        els.results.setAttribute("aria-busy", "false");
        els.count.textContent = "";
    }

    // Les ancres (#lettre-B) n'existent qu'après le premier rendu.
    let hashHandled = false;
    function scrollToHashOnce() {
        if (hashHandled || !window.location.hash) return;
        hashHandled = true;
        const target = document.getElementById(decodeURIComponent(window.location.hash.slice(1)));
        if (target) target.scrollIntoView();
    }

    async function load() {
        showSkeleton();
        try {
            const response = await fetch(config.directoryUrl, {
                credentials: "same-origin",
                headers: { Accept: "application/json" },
            });
            const data = await response.json().catch(() => null);
            if (!response.ok || !data) {
                showLoadError((data && data.message) || "L'annuaire ne répond pas. Réessayez dans quelques instants.");
                return;
            }
            state.entries = data.entries;
            state.cards.clear();
            state.contactsTitle = data.contactsTitle || state.contactsTitle;
            renderServices(data.departments || []);
            if (els.loadedAt && data.loadedAt) {
                const date = new Date(data.loadedAt);
                els.loadedAt.textContent = date.toLocaleTimeString("fr-FR", { hour: "2-digit", minute: "2-digit" });
            }
            render();
            scrollToHashOnce();
        } catch {
            showLoadError("Le serveur ne répond pas. Vérifiez votre connexion puis réessayez.");
        }
    }

    // ---------------------------------------------------------------- Évènements

    let debounce = 0;
    els.search.addEventListener("input", () => {
        updateClear();
        clearTimeout(debounce);
        debounce = setTimeout(() => {
            state.query = els.search.value;
            writeUrl();
            render();
        }, 60);
    });

    els.clear.addEventListener("click", () => {
        els.search.value = "";
        state.query = "";
        updateClear();
        writeUrl();
        render();
        els.search.focus();
    });

    els.service.addEventListener("change", () => {
        state.service = els.service.value;
        syncServiceStyle();
        writeUrl();
        render();
    });

    document.addEventListener("keydown", (event) => {
        const typing = /^(INPUT|SELECT|TEXTAREA)$/.test(document.activeElement?.tagName || "");
        if (event.key === "/" && !typing) {
            event.preventDefault();
            els.search.focus();
            els.search.select();
        } else if (event.key === "Escape" && document.activeElement === els.search && els.search.value) {
            els.clear.click();
        }
    });

    // Filet sous la barre de recherche quand elle « colle » en haut de l'écran.
    if (els.searchbar && "IntersectionObserver" in window) {
        const sentinel = el("div");
        sentinel.style.height = "1px";
        els.searchbar.before(sentinel);
        new IntersectionObserver(([entry]) => {
            els.searchbar.classList.toggle("is-stuck", !entry.isIntersecting);
        }).observe(sentinel);
    }

    readUrl();
    updateClear();
    load();
})();
