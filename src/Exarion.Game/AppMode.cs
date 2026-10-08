// ===========================================================================
//  Etats de l'application (4.1)
// ===========================================================================

namespace Exarion.Game
{
    /// <summary>
    /// Trois etats, et un seul actif a la fois.
    ///
    /// Le point important est que les ENTREES sont dispatchees selon l'etat :
    /// sans ca, la fleche qui deplace une boite dans l'editeur ferait aussi
    /// tourner la camera en partie, et le clic qui selectionne tirerait une
    /// balle.
    /// </summary>
    internal enum AppMode
    {
        Menu,
        Playing,
        Editing,
    }
}
