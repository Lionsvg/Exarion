// ===========================================================================
//  Exarion - Grille de navigation (E16)
//
//  Jusqu'ici un PNJ marchait en LIGNE DROITE vers sa cible et restait bloque
//  contre tout obstacle de plus de 45 cm. C'etait l'absence la plus lourde de
//  l'IA : constate le 17/08, un garde lance en poursuite s'arretait net contre
//  une estrade de 1,80 m, a 0,0 m/s.
//
//  ---------------------------------------------------------------------
//  LA METHODE, et pourquoi pas un navmesh
//
//  Le decor n'est fait QUE de boites alignees. On le decoupe en colonnes
//  verticales de 25 cm de cote ; dans chaque colonne, chaque dessus de boite
//  est un SOL candidat, retenu si le corps du PNJ - sa vraie largeur, sa vraie
//  hauteur - y tient sans toucher aucune boite. Deux sols voisins sont relies
//  si on peut passer de l'un a l'autre : en montant d'au plus une marche, ou
//  en tombant d'au plus MaxDrop. Un A* trouve le chemin, une ligne de vue SUR
//  LA GRILLE le lisse.
//
//  C'est une grille 2,5D : plusieurs etages par colonne (le sol sous une
//  estrade ET le dessus de l'estrade), donc les estrades, les escaliers et les
//  passages dessous fonctionnent. Un navmesh serait plus econome, mais il
//  demande de decouper des polygones - beaucoup de code a se tromper, pour un
//  decor qui n'a que des boites.
//
//  ⚠️ LA GRILLE EST CONSERVATRICE, ET C'EST VOULU. Une case n'est marchable
//  que si le corps y tient CENTRE sur elle : un couloir juste aussi large que
//  le PNJ peut etre juge infranchissable selon l'alignement de la grille. Le
//  contraire - juger marchable ce que le corps ne peut pas traverser - donne
//  un PNJ qui pousse contre un mur en croyant suivre son chemin.
//
//  ---------------------------------------------------------------------
//  ⚠️⚠️ UNE CIBLE INACCESSIBLE ETAIT LE CAS LE PLUS CHER, ET DE LOIN.
//
//  Un A* qui ne trouve pas sa cible ne s'arrete qu'apres avoir visite TOUT ce
//  qui est accessible depuis le depart. Mesure sur 60 x 60 m : 18 ms - plus
//  qu'une image entiere a 64 pas par seconde, pour UN seul PNJ qui regarde un
//  joueur monte sur une estrade sans escalier. Et c'est exactement le cas
//  qu'on rencontre le plus en jeu.
//
//  D'ou deux pieces, calculees une fois a la construction :
//
//   - LE GRAPHE PRECALCULE : les voisins de chaque sol sont trouves une fois
//     pour toutes. La recherche ne refait plus les tests de marche, de chute
//     et de coin a chaque expansion - et surtout, A* et l'analyse ci-dessous
//     lisent LE MEME graphe. Deux definitions des voisins divergeraient.
//
//   - LES COMPOSANTES FORTEMENT CONNEXES (Tarjan) et la table de qui atteint
//     qui. Le graphe est ORIENTE : on descend d'une estrade sans marches, on
//     n'y monte pas. Deux sols dans la meme composante s'atteignent l'un
//     l'autre ; sinon la table le dit. « Cette cible est-elle accessible ? »
//     devient une lecture de bit au lieu d'une inondation de 18 ms.
//
//  Une cible inaccessible ne fait plus qu'une recherche BORNEE
//  (ApproachBudget), qui ramene le chemin vers le point le plus proche
//  d'elle : le garde vient au pied de l'estrade et attend, au lieu de rester
//  plante a son poste ou de pousser contre la paroi.
// ---------------------------------------------------------------------
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using Exarion.Core.Physics;

namespace Exarion.Core.Navigation
{
    /// <summary>Le corps qui se deplace : la grille ne marque une case que si CE corps y tient.</summary>
    public readonly struct NavAgentShape
    {
        public readonly float Width;
        public readonly float Height;

        /// <summary>Plus haute marche franchie sans sauter.</summary>
        public readonly float StepHeight;

        /// <summary>Plus haute chute acceptee dans un chemin. Au-dela, le chemin ne descend pas.</summary>
        public readonly float MaxDrop;

        public NavAgentShape(float width, float height, float stepHeight, float maxDrop)
        {
            Width = width;
            Height = height;
            StepHeight = stepHeight;
            MaxDrop = maxDrop;
        }

        /// <summary>
        /// Les dimensions d'un CharacterMotor : la grille doit decrire le MEME
        /// corps que celui qui marchera. Deux jeux de dimensions divergeraient,
        /// et le PNJ buterait sur des cases que la grille croit libres.
        /// </summary>
        public static NavAgentShape From(MotorSettings settings, float maxDrop = 2f)
            => new(settings.Width, settings.StandHeight, settings.StepHeight, maxDrop);
    }

    /// <summary>Issue d'une recherche de chemin.</summary>
    public enum PathStatus
    {
        /// <summary>Le DEPART n'est sur aucun sol de la grille : il n'y a rien a calculer.</summary>
        NotFound,

        /// <summary>
        /// La cible est HORS D'ATTEINTE. Le chemin mene au point accessible le
        /// plus proche d'elle - parfois le depart lui-meme.
        /// </summary>
        Partial,

        /// <summary>Le chemin mene a la cible.</summary>
        Complete,
    }

    public sealed class NavGrid
    {
        public const float DefaultCellSize = 0.25f;

        /// <summary>Au-dela, A* abandonne : un chemin aussi long est un decor mal fait.</summary>
        private const int MaxExpansions = 200_000;

        /// <summary>
        /// Budget d'une recherche vers une cible INACCESSIBLE : on sait deja
        /// qu'elle echouera, on ne cherche que le point le plus proche.
        ///
        /// 4 096 sols, c'est 256 m² de decor fouille a partir du depart -
        /// moins de 2 ms. Une cible a 40 m est approchee en plusieurs fois :
        /// chaque recalcul repart de plus pres.
        /// </summary>
        public const int ApproachBudget = 4096;

        /// <summary>
        /// Au-dela de ce nombre de composantes, pas de table d'accessibilite :
        /// elle pese N² bits (512 Ko a 2 048). La recherche retombe alors sur
        /// l'A* complet, exact mais cher quand il echoue. Un decor de boites en
        /// produit quelques dizaines - un dessus de caisse par caisse.
        /// </summary>
        private const int MaxReachComponents = 2048;

        /// <summary>
        /// A egalite de cout estime, on prefere le sol le plus AVANCE. Sans ce
        /// departage, sur un sol degage, des milliers de cases ont exactement le
        /// meme cout et l'A* les visite toutes avant d'arriver.
        /// </summary>
        private const float TieBreak = 1e-4f;

        private static readonly float Sqrt2Minus1 = MathF.Sqrt(2f) - 1f;

        private readonly int[] _columnStart;   // Width*Depth + 1 entrees : noeuds de la colonne c = [start[c], start[c+1])
        private readonly float[] _nodeY;       // hauteur du sol, croissante dans une colonne
        private readonly int[] _nodeColumn;
        private readonly float _originX;
        private readonly float _originZ;

        // Le graphe, au format compact : voisins de n = _edges[_edgeStart[n] .. _edgeStart[n+1]).
        private readonly int[] _edgeStart;
        private readonly int[] _edges;

        // Composantes fortement connexes, et qui atteint qui.
        private readonly int[] _component;
        private readonly ulong[]? _reach;
        private readonly int _reachWords;

        // Tampons d'A*, reutilises d'un appel a l'autre : un chemin par PNJ et
        // par demi-seconde ne doit pas allouer des tableaux de 60 000 cases.
        private readonly float[] _g;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private readonly bool[] _closed;
        private int _currentStamp;
        private readonly PriorityQueue<int, float> _open = new();
        private readonly List<Vector3> _raw = new();

        public float CellSize { get; }
        public NavAgentShape Shape { get; }

        /// <summary>Colonnes selon X.</summary>
        public int Width { get; }

        /// <summary>Colonnes selon Z.</summary>
        public int Depth { get; }

        /// <summary>Nombre de sols marchables, tous etages confondus.</summary>
        public int NodeCount => _nodeY.Length;

        /// <summary>Nombre de passages d'un sol a un voisin (marche, montee ou chute).</summary>
        public int EdgeCount => _edges.Length;

        /// <summary>Composantes fortement connexes : un dessus de caisse sans escalier en fait une a lui seul.</summary>
        public int ComponentCount { get; }

        /// <summary>Faux si le decor a trop de composantes pour la table : l'inaccessible coute alors une inondation.</summary>
        public bool HasReachTable => _reach != null;

        /// <summary>Recherches de chemin depuis la construction. Affiche par « nav » - et c'est ce qui trahit un recalcul a chaque pas.</summary>
        public int Searches { get; private set; }

        /// <summary>Sols visites par la derniere recherche.</summary>
        public int LastExpansions { get; private set; }

        private NavGrid(float cellSize, NavAgentShape shape, int width, int depth, float originX, float originZ,
                        int[] columnStart, float[] nodeY, int[] nodeColumn)
        {
            CellSize = cellSize;
            Shape = shape;
            Width = width;
            Depth = depth;
            _originX = originX;
            _originZ = originZ;
            _columnStart = columnStart;
            _nodeY = nodeY;
            _nodeColumn = nodeColumn;

            _g = new float[nodeY.Length];
            _parent = new int[nodeY.Length];
            _stamp = new int[nodeY.Length];
            _closed = new bool[nodeY.Length];

            (_edgeStart, _edges) = BuildEdges();
            _component = BuildComponents(out int components);
            ComponentCount = components;

            if (components <= MaxReachComponents)
            {
                _reachWords = (components + 63) >> 6;
                _reach = BuildReach(components, _reachWords);
            }
        }

        // ===================================================================
        //  Construction
        // ===================================================================

        public static NavGrid Build(CollisionWorld world, NavAgentShape shape, float cellSize = DefaultCellSize)
        {
            ArgumentNullException.ThrowIfNull(world);

            if (cellSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSize));

            var boxes = world.Boxes;

            if (boxes.Count == 0)
                return new NavGrid(cellSize, shape, 0, 0, 0f, 0f, new int[1], Array.Empty<float>(), Array.Empty<int>());

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < boxes.Count; i++)
            {
                minX = MathF.Min(minX, boxes[i].Min.X);
                minZ = MathF.Min(minZ, boxes[i].Min.Z);
                maxX = MathF.Max(maxX, boxes[i].Max.X);
                maxZ = MathF.Max(maxZ, boxes[i].Max.Z);
            }

            int width = Math.Max(1, (int)MathF.Ceiling((maxX - minX) / cellSize));
            int depth = Math.Max(1, (int)MathF.Ceiling((maxZ - minZ) / cellSize));

            var columnStart = new int[width * depth + 1];
            var heights = new List<float>();
            var nodeY = new List<float>();
            var nodeColumn = new List<int>();

            for (int k = 0; k < depth; k++)
            {
                for (int i = 0; i < width; i++)
                {
                    int column = k * width + i;
                    columnStart[column] = nodeY.Count;

                    float x = minX + (i + 0.5f) * cellSize;
                    float z = minZ + (k + 0.5f) * cellSize;

                    // Tous les dessus de boites a la verticale du centre de la
                    // colonne : chacun est un sol possible.
                    heights.Clear();

                    for (int b = 0; b < boxes.Count; b++)
                    {
                        Aabb box = boxes[b];

                        if (x >= box.Min.X && x <= box.Max.X && z >= box.Min.Z && z <= box.Max.Z)
                            heights.Add(box.Max.Y);
                    }

                    heights.Sort();

                    float last = float.NegativeInfinity;

                    foreach (float y in heights)
                    {
                        // Deux boites a la meme hauteur ne font qu'un sol.
                        if (y - last < 1e-4f)
                            continue;

                        last = y;

                        // ⚠️ LE CORPS A SA VRAIE TAILLE, MAIS A PARTIR D'UNE
                        // HAUTEUR DE MARCHE. Un sol sous une estrade basse, ou
                        // contre un mur, n'est pas un endroit ou le PNJ peut se
                        // tenir. En revanche, ce qui depasse de moins d'une
                        // marche ne le gene pas : le CharacterMotor le gravit.
                        //
                        // Premiere version : le corps entier, depuis le sol.
                        // Resultat : devant chaque contremarche, le bas du
                        // corps chevauchait la marche suivante, la case etait
                        // rejetee, et un escalier devenait une suite d'ilots
                        // sans passage. Aucun chemin ne montait plus d'escalier.
                        Aabb body = Aabb.ForCharacter(new Vector3(x, y + shape.StepHeight, z),
                                                      shape.Width, shape.Height - shape.StepHeight);

                        if (!world.Overlaps(body))
                        {
                            nodeY.Add(y);
                            nodeColumn.Add(column);
                        }
                    }
                }
            }

            columnStart[width * depth] = nodeY.Count;

            return new NavGrid(cellSize, shape, width, depth, minX, minZ,
                               columnStart, nodeY.ToArray(), nodeColumn.ToArray());
        }

        private static readonly (int Di, int Dk)[] Directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
        };

        /// <summary>Les voisins de chaque sol, trouves une fois pour toutes.</summary>
        private (int[] Start, int[] Edges) BuildEdges()
        {
            int n = _nodeY.Length;
            var start = new int[n + 1];
            var edges = new List<int>(n * 8);
            Span<int> found = stackalloc int[Directions.Length];

            for (int node = 0; node < n; node++)
            {
                start[node] = edges.Count;

                int count = Neighbors(node, found);

                for (int e = 0; e < count; e++)
                    edges.Add(found[e]);
            }

            start[n] = edges.Count;
            return (start, edges.ToArray());
        }

        /// <summary>
        /// Ou peut-on aller depuis ce sol, en un pas de grille ? C'est LA
        /// definition du deplacement : marche, montee d'une marche, chute.
        /// </summary>
        private int Neighbors(int node, Span<int> output)
        {
            int column = _nodeColumn[node];
            int ci = column % Width, ck = column / Width;
            float y = _nodeY[node];
            int count = 0;

            foreach (var (di, dk) in Directions)
            {
                int i = ci + di, k = ck + dk;

                if (i < 0 || k < 0 || i >= Width || k >= Depth)
                    continue;

                // ⚠️ PAS DE COUPE DE COIN. En diagonale, les deux cases
                // orthogonales doivent etre praticables aussi : sinon le
                // chemin passe entre deux murs qui se touchent par un coin,
                // la ou aucun corps ne passe.
                if (di != 0 && dk != 0
                    && (Reachable(ck * Width + i, y) < 0 || Reachable(k * Width + ci, y) < 0))
                    continue;

                int next = Reachable(k * Width + i, y);

                // Pas de sol voisin : peut-etre un bord d'ou sauter. En ligne
                // droite seulement - une chute en diagonale frolerait l'angle
                // du rebord.
                if (next < 0 && (di == 0 || dk == 0))
                    next = DropTarget(ci, ck, di, dk, y);

                if (next >= 0)
                    output[count++] = next;
            }

            return count;
        }

        /// <summary>
        /// Composantes fortement connexes, par l'algorithme de Tarjan - ecrit
        /// SANS recursion : un sol degage de 60 x 60 m fait une composante de
        /// 57 000 sols, donc une pile d'appels de 57 000 niveaux.
        ///
        /// Les composantes sortent dans l'ordre topologique INVERSE : quand une
        /// composante est emise, toutes celles qu'elle atteint l'ont deja ete.
        /// BuildReach repose entierement sur cette propriete.
        /// </summary>
        private int[] BuildComponents(out int count)
        {
            int n = _nodeY.Length;
            var component = new int[n];
            var index = new int[n];        // 0 = pas encore vu ; sinon ordre de decouverte + 1
            var low = new int[n];
            var onStack = new bool[n];
            var stack = new int[n];
            var callNode = new int[n];
            var callEdge = new int[n];
            int top = 0, counter = 0;

            count = 0;

            for (int root = 0; root < n; root++)
            {
                if (index[root] != 0)
                    continue;

                int depth = 0;
                callNode[0] = root;
                callEdge[0] = _edgeStart[root];
                index[root] = low[root] = ++counter;
                stack[top++] = root;
                onStack[root] = true;

                while (depth >= 0)
                {
                    int v = callNode[depth];

                    if (callEdge[depth] < _edgeStart[v + 1])
                    {
                        int w = _edges[callEdge[depth]++];

                        if (index[w] == 0)
                        {
                            index[w] = low[w] = ++counter;
                            stack[top++] = w;
                            onStack[w] = true;

                            depth++;
                            callNode[depth] = w;
                            callEdge[depth] = _edgeStart[w];
                        }
                        else if (onStack[w])
                        {
                            low[v] = Math.Min(low[v], index[w]);
                        }

                        continue;
                    }

                    // v est termine : s'il est la racine de sa composante, on
                    // depile toute la composante.
                    if (low[v] == index[v])
                    {
                        int w;

                        do
                        {
                            w = stack[--top];
                            onStack[w] = false;
                            component[w] = count;
                        }
                        while (w != v);

                        count++;
                    }

                    depth--;

                    if (depth >= 0)
                    {
                        int parent = callNode[depth];
                        low[parent] = Math.Min(low[parent], low[v]);
                    }
                }
            }

            return component;
        }

        /// <summary>
        /// Pour chaque composante, l'ensemble des composantes qu'elle atteint,
        /// en bits. Traitees dans l'ordre d'emission de Tarjan : une composante
        /// n'atteint que des composantes deja completes.
        /// </summary>
        private ulong[] BuildReach(int components, int words)
        {
            int n = _nodeY.Length;

            // Les sols regroupes par composante (tri par denombrement).
            var memberStart = new int[components + 1];

            for (int node = 0; node < n; node++)
                memberStart[_component[node] + 1]++;

            for (int c = 0; c < components; c++)
                memberStart[c + 1] += memberStart[c];

            var members = new int[n];
            var fill = (int[])memberStart.Clone();

            for (int node = 0; node < n; node++)
                members[fill[_component[node]]++] = node;

            var reach = new ulong[components * words];

            for (int c = 0; c < components; c++)
            {
                int row = c * words;
                reach[row + (c >> 6)] |= 1UL << (c & 63);

                for (int m = memberStart[c]; m < memberStart[c + 1]; m++)
                {
                    int node = members[m];

                    for (int e = _edgeStart[node]; e < _edgeStart[node + 1]; e++)
                    {
                        int other = _component[_edges[e]];

                        if (other == c)
                            continue;

                        int source = other * words;

                        for (int w = 0; w < words; w++)
                            reach[row + w] |= reach[source + w];
                    }
                }
            }

            return reach;
        }

        // ===================================================================
        //  Interrogation
        // ===================================================================

        /// <summary>Position des pieds sur un sol de la grille.</summary>
        public Vector3 PositionOf(int node)
        {
            int column = _nodeColumn[node];
            int i = column % Width;
            int k = column / Width;

            return new Vector3(_originX + (i + 0.5f) * CellSize, _nodeY[node], _originZ + (k + 0.5f) * CellSize);
        }

        private bool ColumnOf(float x, float z, out int i, out int k)
        {
            i = (int)MathF.Floor((x - _originX) / CellSize);
            k = (int)MathF.Floor((z - _originZ) / CellSize);

            return i >= 0 && k >= 0 && i < Width && k < Depth;
        }

        /// <summary>
        /// Le sol sur lequel se tiennent des pieds dans UNE colonne : le plus
        /// haut qui ne soit pas au-dessus d'eux de plus d'une marche. -1 sinon.
        /// </summary>
        private int FloorInColumn(int column, float feetY)
        {
            int best = -1;

            for (int n = _columnStart[column]; n < _columnStart[column + 1]; n++)
                if (_nodeY[n] <= feetY + Shape.StepHeight)
                    best = n;   // croissant : le dernier retenu est le plus haut

            return best;
        }

        /// <summary>
        /// Le sol de la grille le plus proche de ces pieds, ou -1.
        ///
        /// D'abord la colonne sous les pieds ; sinon les colonnes voisines, sur
        /// deux cases. Un PNJ colle a un mur a son centre dans une case jugee
        /// non marchable (son corps y frotte le mur) : sans ce repli, il ne
        /// trouverait plus aucun chemin des qu'il longe une paroi.
        /// </summary>
        public int FindNode(Vector3 feet)
        {
            if (Width == 0)
                return -1;

            ColumnOf(feet.X, feet.Z, out int ci, out int ck);

            int best = -1;
            float bestDistance = float.MaxValue;

            for (int radius = 0; radius <= 2 && best < 0; radius++)
            {
                for (int dk = -radius; dk <= radius; dk++)
                {
                    for (int di = -radius; di <= radius; di++)
                    {
                        // Seulement l'anneau : l'interieur a deja ete vu.
                        if (Math.Max(Math.Abs(di), Math.Abs(dk)) != radius)
                            continue;

                        int i = ci + di, k = ck + dk;

                        if (i < 0 || k < 0 || i >= Width || k >= Depth)
                            continue;

                        int n = FloorInColumn(k * Width + i, feet.Y);

                        if (n < 0)
                            continue;

                        float d = Vector3.DistanceSquared(PositionOf(n), feet);

                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = n;
                        }
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Peut-on aller de ces pieds a ceux-la ? Une lecture de bit quand la
        /// table existe ; sinon, faute de savoir, on repond oui et la recherche
        /// tranchera.
        /// </summary>
        public bool CanReach(Vector3 from, Vector3 to)
        {
            int a = FindNode(from), b = FindNode(to);
            return a >= 0 && b >= 0 && CanReach(a, b);
        }

        private bool CanReach(int from, int to)
        {
            int a = _component[from], b = _component[to];

            if (a == b)
                return true;

            if (_reach is null)
                return true;

            return (_reach[a * _reachWords + (b >> 6)] & (1UL << (b & 63))) != 0;
        }

        /// <summary>
        /// Peut-on marcher en ligne droite de a vers b sans quitter la grille ?
        ///
        /// On echantillonne le segment tous les huitiemes de case : chaque
        /// echantillon doit tomber sur un sol, a une marche au plus de
        /// l'echantillon precedent. AUCUNE chute acceptee ici : la ligne de vue
        /// sert a COUPER un chemin, et un raccourci qui saute d'une estrade
        /// n'est pas celui que l'A* avait choisi.
        ///
        /// ⚠️ ET ON DOIT ARRIVER AU NIVEAU DE b. Sans ce dernier test, un joueur
        /// sur une passerelle est « en ligne droite » de tout le sol qui passe
        /// dessous : le PNJ marcherait jusque sous ses pieds et s'y arreterait.
        /// </summary>
        public bool LineWalkable(Vector3 a, Vector3 b)
        {
            if (Width == 0)
                return false;

            int start = FindNode(a);
            int end = FindNode(b);

            if (start < 0 || end < 0)
                return false;

            float y = _nodeY[start];

            var flat = new Vector2(b.X - a.X, b.Z - a.Z);
            float length = flat.Length();
            float spacing = CellSize * 0.125f;
            int samples = Math.Max(1, (int)MathF.Ceiling(length / spacing));

            for (int s = 1; s <= samples; s++)
            {
                float t = s / (float)samples;

                if (!ColumnOf(a.X + flat.X * t, a.Z + flat.Y * t, out int i, out int k))
                    return false;

                int column = k * Width + i;
                int found = -1;

                for (int n = _columnStart[column]; n < _columnStart[column + 1]; n++)
                {
                    if (MathF.Abs(_nodeY[n] - y) <= Shape.StepHeight)
                    {
                        found = n;
                        break;
                    }
                }

                if (found < 0)
                    return false;

                y = _nodeY[found];
            }

            return MathF.Abs(y - _nodeY[end]) <= Shape.StepHeight;
        }

        // ===================================================================
        //  A*
        // ===================================================================

        /// <summary>Un sol de la colonne voisine atteignable depuis un sol a la hauteur y, ou -1.</summary>
        private int Reachable(int column, float y)
        {
            int best = -1;
            float bestGap = float.MaxValue;

            for (int n = _columnStart[column]; n < _columnStart[column + 1]; n++)
            {
                float dy = _nodeY[n] - y;

                // Monter d'une marche au plus ; descendre de MaxDrop au plus.
                if (dy > Shape.StepHeight || dy < -Shape.MaxDrop)
                    continue;

                // Plusieurs sols possibles : le plus proche en hauteur. Monter
                // sur un sol plus bas que soi n'a pas de sens si un autre est a
                // niveau.
                float gap = MathF.Abs(dy);

                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = n;
                }
            }

            return best;
        }

        /// <summary>
        /// Ou atterrit-on en sautant d'un bord, dans cette direction ? -1 si
        /// ce n'est pas un bord ou s'il n'y a rien en dessous.
        ///
        /// ⚠️ POURQUOI UN SAUT DE PLUSIEURS CASES. Au pied d'un mur, sur une
        /// largeur de demi-corps, le sol n'est PAS marchable : le corps y
        /// toucherait la paroi. Le dessus d'une estrade n'avait donc aucune
        /// case voisine au sol, et la premiere version ne savait pas en
        /// redescendre. Un vrai PNJ qui sort d'un bord tombe le long du mur et
        /// atterrit a un demi-corps de lui : c'est exactement cette arete-ci.
        /// </summary>
        private int DropTarget(int ci, int ck, int di, int dk, float y)
        {
            int reach = (int)MathF.Ceiling(Shape.Width / CellSize) + 1;

            for (int d = 2; d <= reach; d++)
            {
                int i = ci + di * d, k = ck + dk * d;

                if (i < 0 || k < 0 || i >= Width || k >= Depth)
                    return -1;

                int column = k * Width + i;
                int landing = -1;

                for (int n = _columnStart[column]; n < _columnStart[column + 1]; n++)
                {
                    float dy = _nodeY[n] - y;

                    // Un sol a hauteur de marche : ce n'etait pas un bord, la
                    // marche ordinaire s'en charge.
                    if (MathF.Abs(dy) <= Shape.StepHeight)
                        return -1;

                    // Le plus haut des sols en contrebas atteignables : on
                    // atterrit sur le premier qu'on rencontre en tombant.
                    if (dy < -Shape.StepHeight && dy >= -Shape.MaxDrop)
                        landing = n;
                }

                if (landing >= 0)
                    return landing;
            }

            return -1;
        }

        /// <summary>
        /// Distance « octile » au sol : la longueur exacte d'un trajet en huit
        /// directions sur un sol degage. Jamais plus longue que le vrai chemin
        /// (chaque pas de grille est droit ou en diagonale exacte, et une chute
        /// ne fait que l'allonger) : l'A* reste optimal.
        ///
        /// La distance a vol d'oiseau serait juste aussi, mais plus courte que
        /// tout trajet en huit directions : l'A* croirait chaque detour possible
        /// et fouillerait une bande entiere de cases equivalentes.
        /// </summary>
        private static float Heuristic(Vector3 a, Vector3 b)
        {
            float dx = MathF.Abs(a.X - b.X), dz = MathF.Abs(a.Z - b.Z);
            return MathF.Max(dx, dz) + Sqrt2Minus1 * MathF.Min(dx, dz);
        }

        /// <summary>
        /// Un chemin de a vers b, en positions de pieds, lisse.
        ///
        /// Complete : il mene a b. Partial : b est HORS D'ATTEINTE, et le chemin
        /// mene au plus pres - le PNJ ne doit surtout pas foncer droit sur sa
        /// cible, il pousserait contre un mur en croyant avancer. NotFound : le
        /// depart lui-meme n'est sur aucun sol, le chemin est vide.
        /// </summary>
        public PathStatus FindPath(Vector3 from, Vector3 to, List<Vector3> path)
        {
            ArgumentNullException.ThrowIfNull(path);
            path.Clear();

            Searches++;
            LastExpansions = 0;

            int start = FindNode(from);

            if (start < 0)
                return PathStatus.NotFound;

            int goal = FindNode(to);

            if (goal == start)
            {
                path.Add(PositionOf(goal));
                return PathStatus.Complete;
            }

            // La question cle, posee AVANT de chercher. Une cible hors de la
            // grille (aucun sol sous elle) est inaccessible par definition : on
            // vise alors le point brut, pour s'en approcher.
            bool reachable = goal >= 0 && CanReach(start, goal);
            Vector3 target = goal >= 0 ? PositionOf(goal) : to;

            int end = Search(start, reachable ? goal : -1, target,
                             reachable ? MaxExpansions : ApproachBudget, out bool found);

            _raw.Clear();

            for (int n = end; n >= 0; n = _parent[n])
                _raw.Add(PositionOf(n));

            _raw.Reverse();

            Smooth(_raw, path);
            return found ? PathStatus.Complete : PathStatus.Partial;
        }

        /// <summary>
        /// L'A* lui-meme. Rend le but s'il l'atteint ; sinon le sol visite le
        /// plus proche de la cible - c'est ce qui fait le chemin partiel.
        /// </summary>
        private int Search(int start, int goal, Vector3 target, int budget, out bool found)
        {
            // Une estampille par appel : pas besoin de remettre les tableaux a
            // zero, une case dont l'estampille est ancienne est « non vue ».
            if (++_currentStamp == int.MaxValue)
            {
                Array.Clear(_stamp);
                _currentStamp = 1;
            }

            int stamp = _currentStamp;

            _open.Clear();
            Visit(start, 0f, -1, stamp);
            _open.Enqueue(start, Heuristic(PositionOf(start), target));

            int best = start;
            float bestDistance = Vector3.Distance(PositionOf(start), target);
            int expansions = 0;

            found = false;

            while (_open.TryDequeue(out int current, out _))
            {
                if (_closed[current])
                    continue;

                _closed[current] = true;

                if (current == goal)
                {
                    found = true;
                    best = goal;
                    break;
                }

                Vector3 here = PositionOf(current);
                float distance = Vector3.Distance(here, target);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = current;
                }

                if (++expansions > budget)
                    break;

                for (int e = _edgeStart[current]; e < _edgeStart[current + 1]; e++)
                {
                    int next = _edges[e];

                    if (_stamp[next] == stamp && _closed[next])
                        continue;

                    Vector3 there = PositionOf(next);
                    float g = _g[current] + Vector3.Distance(here, there);

                    if (_stamp[next] == stamp && g >= _g[next])
                        continue;

                    Visit(next, g, current, stamp);
                    _open.Enqueue(next, g * (1f - TieBreak) + Heuristic(there, target));
                }
            }

            LastExpansions = expansions;
            return best;
        }

        private void Visit(int node, float g, int parent, int stamp)
        {
            _stamp[node] = stamp;
            _g[node] = g;
            _parent[node] = parent;
            _closed[node] = false;
        }

        /// <summary>
        /// Tire le fil : depuis chaque point retenu, on saute au plus lointain
        /// encore atteignable en ligne droite. L'A* sur une grille donne des
        /// escaliers de cases ; un PNJ qui les suivrait zigzaguerait.
        /// </summary>
        private void Smooth(List<Vector3> raw, List<Vector3> path)
        {
            path.Add(raw[0]);

            int i = 0;

            while (i < raw.Count - 1)
            {
                int j = i + 1;

                while (j + 1 < raw.Count && LineWalkable(raw[i], raw[j + 1]))
                    j++;

                path.Add(raw[j]);
                i = j;
            }
        }
    }
}
