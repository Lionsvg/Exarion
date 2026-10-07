// ===========================================================================
//  Exarion - Hote du moteur
//
//  Cree la fenetre, ouvre le contexte GPU, et fait tourner la boucle.
//
//  Point cle : la boucle N'UTILISE PAS le delta fourni par la fenetre pour
//  simuler. Elle interroge un Stopwatch et passe le temps ABSOLU a
//  FixedTimestep. C'est ce qui garantit que la simulation avance de la
//  meme facon quelle que soit la machine - condition necessaire au reseau.
//
//  Le rendu, lui, tourne aussi vite que possible et interpole avec Alpha.
// ===========================================================================

using System;
using System.Diagnostics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Exarion.Core;

namespace Exarion.Engine
{
    public sealed class EngineOptions
    {
        public string Title = "Exarion";
        public int Width = 1280;
        public int Height = 720;
        public double TickRate = 64.0;
        public int MaxStepsPerFrame = 5;
        public bool VSync = true;

        /// <summary>
        /// Echantillons d'anticrenelage (MSAA). 0 ou 1 = desactive.
        /// C'est le reglage qui change le plus l'impression de finition :
        /// des aretes en escalier trahissent immediatement un prototype.
        /// </summary>
        public int Samples = 4;

        /// <summary>
        /// Si > 0, la fenetre se ferme apres ce nombre de secondes.
        /// Sert a valider le moteur en ligne de commande, sans intervention.
        /// </summary>
        public double AutoCloseAfterSeconds = 0.0;
    }

    public sealed class EngineHost : IDisposable
    {
        private readonly EngineOptions _options;
        private readonly Stopwatch _clock = new();
        private IWindow? _window;
        private IInputContext? _input;
        private GL? _gl;

        private long _framesRendered;
        private double _lastStatsTime;
        private long _framesAtLastStats;
        private long _stepsAtLastStats;

        public FixedTimestep Time { get; }
        public GL Gl => _gl ?? throw new InvalidOperationException("Le contexte GPU n'est pas encore cree.");
        public IInputContext Input => _input ?? throw new InvalidOperationException("Les entrees ne sont pas encore pretes.");
        public IWindow Window => _window ?? throw new InvalidOperationException("La fenetre n'est pas encore creee.");

        /// <summary>Appele une fois, quand le contexte GPU est pret.</summary>
        public event Action? Load;

        /// <summary>
        /// Un pas de simulation de duree fixe. C'est ICI que va toute la
        /// logique de jeu. Recoit le dt fixe et le numero de tick.
        /// </summary>
        public event Action<double, long>? Simulate;

        /// <summary>
        /// Rendu. alpha dans [0,1[ : facteur d'interpolation entre le pas
        /// precedent et le pas courant. Aucune logique de jeu ici.
        /// </summary>
        public event Action<float>? Render;

        public event Action? Unload;
        public event Action<Vector2D<int>>? Resized;

        public EngineHost(EngineOptions? options = null)
        {
            _options = options ?? new EngineOptions();
            Time = new FixedTimestep(_options.TickRate, _options.MaxStepsPerFrame);
        }

        public void Run()
        {
            var wo = WindowOptions.Default;
            wo.Title = _options.Title;
            wo.Size = new Vector2D<int>(_options.Width, _options.Height);
            wo.VSync = _options.VSync;
            // On veut un contexte moderne : OpenGL 3.3 core est le plancher
            // raisonnable, supporte partout depuis 2010.
            wo.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default,
                                     new APIVersion(3, 3));

            if (_options.Samples > 1)
                wo.Samples = _options.Samples;

            _window = Silk.NET.Windowing.Window.Create(wo);

            _window.Load += OnLoad;
            _window.Update += OnUpdate;
            _window.Render += OnRender;
            _window.Closing += OnClosing;
            _window.FramebufferResize += OnResize;

            _window.Run();
        }

        private void OnLoad()
        {
            _gl = GL.GetApi(_window);
            _input = _window!.CreateInput();

            LogDiagnostics();

            _gl.Enable(EnableCap.DepthTest);
            _gl.ClearColor(0.09f, 0.10f, 0.12f, 1f);

            if (_options.Samples > 1)
                _gl.Enable(EnableCap.Multisample);

            // Le viewport doit etre pose des le depart : on ne peut pas
            // compter sur un evenement de redimensionnement pour le faire.
            // On utilise la taille du FRAMEBUFFER, pas celle de la fenetre :
            // elles different sur un ecran a mise a l'echelle DPI.
            _gl.Viewport(_window!.FramebufferSize);
            Resized?.Invoke(_window.FramebufferSize);

            _clock.Start();
            Load?.Invoke();
        }

        private void OnUpdate(double _)
        {
            // On ignore volontairement le delta fourni par la fenetre.
            int steps = Time.AdvanceTo(_clock.Elapsed);

            for (int i = 0; i < steps; i++)
                Simulate?.Invoke(Time.FixedDelta, Time.TotalSteps - steps + i + 1);

            if (_options.AutoCloseAfterSeconds > 0.0 &&
                _clock.Elapsed.TotalSeconds >= _options.AutoCloseAfterSeconds)
            {
                _window!.Close();
            }
        }

        private void OnRender(double _)
        {
            _gl!.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            Render?.Invoke(Time.Alpha);

            _framesRendered++;
            ReportStats();
        }

        private void OnResize(Vector2D<int> size)
        {
            _gl?.Viewport(size);
            Resized?.Invoke(size);
        }

        private void OnClosing()
        {
            Unload?.Invoke();

            // On libere ici ET on annule la reference : Dispose() est appele
            // ensuite par le using cote appelant, et Silk.NET leve une
            // ObjectDisposedException si on libere le contexte deux fois.
            _input?.Dispose();
            _input = null;
        }

        /// <summary>
        /// Trace ce que la machine annonce. Indispensable : c'est la seule
        /// facon de diagnostiquer un probleme de rendu sans voir l'ecran.
        /// </summary>
        private unsafe void LogDiagnostics()
        {
            string Get(StringName n)
            {
                byte* p = _gl!.GetString(n);
                return p == null ? "?" : new string((sbyte*)p);
            }

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("  Exarion Engine");
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine($"  Fabricant GPU : {Get(StringName.Vendor)}");
            Console.WriteLine($"  Carte         : {Get(StringName.Renderer)}");
            Console.WriteLine($"  OpenGL        : {Get(StringName.Version)}");
            Console.WriteLine($"  GLSL          : {Get(StringName.ShadingLanguageVersion)}");
            Console.WriteLine($"  Tickrate      : {Time.TickRate:0.##} Hz  (pas = {Time.FixedDeltaNanoseconds / 1_000_000.0:0.###} ms)");
            Console.WriteLine($"  VSync         : {_options.VSync}");
            Console.WriteLine("--------------------------------------------------");
        }

        /// <summary>Une ligne par seconde : images/s, pas/s, pas sautes.</summary>
        private void ReportStats()
        {
            double now = _clock.Elapsed.TotalSeconds;
            if (now - _lastStatsTime < 1.0)
                return;

            double span = now - _lastStatsTime;
            double fps = (_framesRendered - _framesAtLastStats) / span;
            double tps = (Time.TotalSteps - _stepsAtLastStats) / span;

            Console.WriteLine($"[{now,6:0.0} s]  {fps,6:0.0} img/s   {tps,6:0.0} pas/s   sautes: {Time.SkippedSteps}");

            _lastStatsTime = now;
            _framesAtLastStats = _framesRendered;
            _stepsAtLastStats = Time.TotalSteps;
        }

        public void Dispose()
        {
            _input?.Dispose();
            _input = null;

            _window?.Dispose();
            _window = null;
        }
    }
}
