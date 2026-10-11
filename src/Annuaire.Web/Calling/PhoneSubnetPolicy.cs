using System.Net;
using System.Net.Sockets;
using Annuaire.Web.Options;
using Microsoft.Extensions.Options;

namespace Annuaire.Web.Calling;

/// <summary>
/// Vérifie qu'une IP lue dans l'AD est bien celle d'un téléphone (adresse IPv4/IPv6 valide, dans un réseau autorisé).
/// Empêche qu'une valeur erronée fasse envoyer par le serveur des requêtes vers d'autres machines.
/// </summary>
public sealed class PhoneSubnetPolicy
{
    private readonly IReadOnlyList<IPNetwork> _networks;

    public PhoneSubnetPolicy(IOptions<CallingOptions> options)
    {
        var networks = new List<IPNetwork>();
        foreach (var cidr in options.Value.AllowedPhoneSubnets.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (!IPNetwork.TryParse(cidr.Trim(), out var network))
            {
                throw new InvalidOperationException(
                    $"Calling:AllowedPhoneSubnets contient une valeur invalide : « {cidr} » (format attendu : 10.20.0.0/16).");
            }

            networks.Add(network);
        }

        _networks = networks;
    }

    public bool HasRestriction => _networks.Count > 0;

    public bool IsAllowed(string? phoneIp)
    {
        if (string.IsNullOrWhiteSpace(phoneIp)
            || !IPAddress.TryParse(phoneIp.Trim(), out var address)
            || address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            // IPAddress.TryParse accepte des formes ambiguës (ex. "10.1") : on exige l'écriture canonique.
            || !string.Equals(address.ToString(), phoneIp.Trim(), StringComparison.OrdinalIgnoreCase)
            || IPAddress.IsLoopback(address))
        {
            return false;
        }

        return _networks.Count == 0 || _networks.Any(n => n.Contains(address));
    }
}
