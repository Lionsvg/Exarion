// ===========================================================================
//  Exarion - Le client joue sur la carte du SERVEUR
//
//  La prediction (3.5) exige que le client simule dans le MEME decor que le
//  serveur. Jusqu'au 08/10, le serveur annoncait le NOM de sa carte et le
//  client l'affichait - rien de plus : un client sur une autre carte predisait
//  dans son propre decor, et se faisait corriger en permanence sans que rien
//  ne dise pourquoi.
//
//  Le nom ne suffit pas, et c'est le coeur de ce fichier. Deux « arene.map »
//  peuvent differer d'un mur : meme nom, autre decor, et la meme correction
//  permanente, plus difficile encore a attribuer. Le serveur envoie donc une
//  EMPREINTE du contenu (MapFile.Fingerprint) ; le client ne garde ou ne
//  charge une carte que si l'empreinte concorde, et sinon il REFUSE - en le
//  disant.
//
//  ⚠️ Ici la DECISION seulement, en fonction pure : elle se teste en ligne de
//  commande. Charger, appliquer et deconnecter sont le travail du jeu.
// ===========================================================================

using System;
using System.Collections.Generic;
using Exarion.Core.Assets;

namespace Exarion.Core.Net
{
    public enum MapSyncAction
    {
        /// <summary>La carte courante EST celle du serveur : rien a faire.</summary>
        Keep,

        /// <summary>La carte du serveur existe ici, contenu identique : la charger.</summary>
        Load,

        /// <summary>Introuvable ici, ou differente : jouer serait jouer faux.</summary>
        Refuse,
    }

    public static class MapSync
    {
        /// <summary>
        /// Que faire de la carte annoncee par le serveur ?
        /// </summary>
        /// <param name="findLocal">
        /// Les cartes locales qui portent ce nom, dans l'ordre de preference.
        /// PLUSIEURS, parce qu'un nom peut designer a la fois une carte integree
        /// et un fichier homonyme : le bac a sable integre s'appelle « bac a
        /// sable », et un « bac_a_sable.map » peut trainer dans le dossier.
        /// </param>
        /// <param name="toLoad">La carte a charger quand la reponse est Load.</param>
        /// <param name="reason">Une phrase pour l'utilisateur, dans tous les cas.</param>
        public static MapSyncAction Decide(string serverName, uint serverFingerprint, MapData? current,
                                           Func<string, IEnumerable<MapData>> findLocal,
                                           out MapData? toLoad, out string reason)
        {
            ArgumentNullException.ThrowIfNull(findLocal);

            toLoad = null;

            if (current != null && MapFile.Fingerprint(current) == serverFingerprint)
            {
                reason = $"carte du serveur : {serverName}, identique a la carte courante";
                return MapSyncAction.Keep;
            }

            uint? firstMismatch = null;

            if (!string.IsNullOrWhiteSpace(serverName))
            {
                foreach (MapData candidate in findLocal(serverName))
                {
                    uint mine = MapFile.Fingerprint(candidate);

                    if (mine == serverFingerprint)
                    {
                        toLoad = candidate;
                        reason = $"carte du serveur chargee : {serverName}";
                        return MapSyncAction.Load;
                    }

                    firstMismatch ??= mine;
                }
            }

            if (firstMismatch is uint other)
            {
                // ⚠️ Le cas qui justifie l'empreinte : meme nom, autre decor.
                reason = $"« {serverName} » existe ici mais son decor DIFFERE de celui du serveur " +
                         $"(empreinte {other:x8} contre {serverFingerprint:x8}). La prediction serait fausse.";
                return MapSyncAction.Refuse;
            }

            reason = $"carte du serveur INTROUVABLE ici : « {serverName} ». La prediction serait fausse.";
            return MapSyncAction.Refuse;
        }
    }
}
