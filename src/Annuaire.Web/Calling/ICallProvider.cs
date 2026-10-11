namespace Annuaire.Web.Calling;

/// <summary>Fait composer un numéro par le poste de l'appelant.</summary>
public interface ICallProvider
{
    /// <summary>Nom technique, repris dans le journal des appels.</summary>
    string Name { get; }

    /// <summary>Faux pour le fournisseur désactivé.</summary>
    bool IsEnabled => true;

    /// <summary>L'appelant doit avoir un poste (ipPhone) dans l'AD.</summary>
    bool RequiresCallerExtension => true;

    /// <summary>L'appelant doit avoir l'IP de son téléphone dans l'AD.</summary>
    bool RequiresPhoneIp { get; }

    /// <param name="caller">Appelant identifié côté serveur.</param>
    /// <param name="number">Numéro déjà normalisé, prêt à composer.</param>
    Task<CallResult> DialAsync(CallerInfo caller, string number);
}
