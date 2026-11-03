using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

/// <summary>
/// Ejecuta el precalentamiento de los enemigos (EnemyWarmup) mientras el jugador está en el menú, repartido entre fotogramas:
///   1. Tras un instante (StartDelay), y una vez terminado el precalentamiento de la ambientación si hay uno en marcha (así no compiten
///      por el mismo fotograma), crea un SubViewport propio que nadie ve, con cámara, una luz con sombra y un suelo, como las salas.
///   2. Carga la escena de cada tipo (EnemyScenePreloader), la instancia, la deja preparada (EnemyBase.Stage) y la cuelga del visor, de uno
///      en uno y con un presupuesto de tiempo por fotograma.
///   3. Los revela a la vez (EnemyBase.BeginSpawn, y con ellos un anillo SpawnMarker) y los deja dibujarse hasta que terminan su aparición inerte: así se calientan también
///      el JIT del propio camino de aparición y la subida de mallas y texturas de cada tipo. Después libera el visor.
/// Los enemigos del visor no hacen nada más (no hay jugador) y se quitan del grupo de voladores para que su aleteo no suene en el menú.
/// Si el menú se cierra antes de terminar, el nodo desaparece con él y la siguiente vez continúa donde quedó. No hace nada si ya se
/// terminó (EnemyWarmup.Done) o si EnemyWarmup.Enabled es false.
/// </summary>
public partial class EnemyWarmupRunner : Node
{
	public const string NodeName = "EnemyWarmup";

	/// <summary>Segundos de espera antes de empezar, para que el menú aparezca sin competir con las cargas.</summary>
	[Export] public float StartDelay { get; set; } = 0.25f;

	/// <summary>Milisegundos por fotograma que se dedican a cargar e instanciar (siempre se hace al menos un tipo).</summary>
	[Export] public double FrameBudgetMs { get; set; } = 6.0;

	/// <summary>Fotogramas mínimos que se dibuja el visor con los enemigos revelados.</summary>
	[Export] public int GpuFrames { get; set; } = 3;

	/// <summary>Tope de fotogramas de dibujo, por si la aparición inerte no terminara (por ejemplo, con el juego en pausa).</summary>
	[Export] public int MaxDrawFrames { get; set; } = 120;

	public bool Finished { get; private set; }
	public event Action Completed;

	private enum Stage { Delay, Loading, Drawing }

	private Stage _stage = Stage.Delay;
	private float _elapsed;
	private int _next;
	private int _drawn;
	private SubViewport _viewport;
	private SpawnMarker _marker;
	private readonly List<EnemyBase> _enemies = new();

	/// <summary>Cuelga un runner de parent si hace falta (devuelve null si ya está todo hecho, está apagado o ya hay uno).</summary>
	public static EnemyWarmupRunner Attach(Node parent)
	{
		if (!EnemyWarmup.Enabled || EnemyWarmup.Done || parent.GetNodeOrNull(NodeName) != null)
		{
			return null;
		}

		EnemyWarmupRunner runner = new() { Name = NodeName };
		parent.AddChild(runner);
		return runner;
	}

	public override void _Process(double delta)
	{
		switch (_stage)
		{
			case Stage.Delay:
				_elapsed += (float)delta;
				if (_elapsed >= StartDelay && !DecorWarmupRunning())
				{
					BuildViewport();
					_stage = Stage.Loading;
				}

				break;
			case Stage.Loading:
				if (!LoadSome())
				{
					RevealAll();
					_stage = Stage.Drawing;
				}

				break;
			case Stage.Drawing:
				_drawn++;
				if (_drawn >= MaxDrawFrames || (_drawn >= GpuFrames && AllAppeared()))
				{
					Finish();
				}

				break;
		}
	}

	// Hay un precalentamiento de la ambientación hermano que aún no terminó: se espera a que acabe.
	private bool DecorWarmupRunning()
	{
		return DecorWarmup.Enabled && !DecorWarmup.Done && GetParent()?.GetNodeOrNull(DecorWarmupRunner.NodeName) != null;
	}

	// Carga e instancia tipos hasta gastar el presupuesto del fotograma. Devuelve true si aún quedan.
	private bool LoadSome()
	{
		Stopwatch clock = Stopwatch.StartNew();
		do
		{
			if (_next >= EnemyKinds.List.Count)
			{
				break;
			}

			EnemyKind kind = EnemyKinds.List[_next++].Kind;
			if (!EnemyScenePreloader.Preload(kind) || EnemyKinds.Scene(kind).Instantiate() is not EnemyBase enemy)
			{
				EnemyWarmup.MarkFailed();
				continue;
			}

			enemy.Stage();
			_viewport.AddChild(enemy);
			enemy.RemoveFromGroup(AudioManager.FlyerGroup);
			int index = _enemies.Count;
			enemy.Position = new Vector3(-3.0f + (index % 3) * 3.0f, 0.5f, -1.5f + (index / 3) * 3.0f);
			_enemies.Add(enemy);
		}
		while (clock.Elapsed.TotalMilliseconds < FrameBudgetMs);

		return _next < EnemyKinds.List.Count;
	}

	private void RevealAll()
	{
		_marker?.Finish();
		foreach (EnemyBase enemy in _enemies)
		{
			enemy.BeginSpawn();
		}
	}

	private bool AllAppeared()
	{
		foreach (EnemyBase enemy in _enemies)
		{
			if (enemy.Spawning)
			{
				return false;
			}
		}

		return true;
	}

	private void Finish()
	{
		foreach (EnemyBase enemy in _enemies)
		{
			EnemyWarmup.MarkWarmed(enemy.Kind);
		}

		_viewport?.QueueFree();
		_viewport = null;
		_marker = null;
		_enemies.Clear();
		EnemyWarmup.MarkDone();
		Finished = true;
		Completed?.Invoke();
		SetProcess(false);
		QueueFree();
	}

	// Escena mínima, a la vista de una cámara ortogonal: un suelo, una luz direccional con sombra y una omni, como en las salas.
	private void BuildViewport()
	{
		_viewport = new SubViewport
		{
			Name = "EnemyWarmupViewport",
			Size = new Vector2I(256, 256),
			OwnWorld3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		AddChild(_viewport);

		_viewport.AddChild(new Camera3D
		{
			Projection = Camera3D.ProjectionType.Orthogonal,
			Size = 10.0f,
			Current = true,
			Transform = new Transform3D(Basis.LookingAt(new Vector3(0.0f, -1.0f, -0.35f), Vector3.Up), new Vector3(0.0f, 10.0f, 3.5f)),
		});
		_viewport.AddChild(new DirectionalLight3D { ShadowEnabled = true, Transform = new Transform3D(Basis.LookingAt(new Vector3(-0.3f, -1.0f, -0.4f), Vector3.Up), Vector3.Zero) });
		_viewport.AddChild(new OmniLight3D { Position = new Vector3(0.0f, 3.0f, 0.0f), OmniRange = 12.0f });
		// Un anillo de aparición, para dejar listas también sus mallas compartidas y su camino de código.
		_marker = new SpawnMarker { Position = new Vector3(0.0f, 0.03f, 3.5f) };
		_viewport.AddChild(_marker);
		_viewport.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(14.0f, 1.0f, 10.0f) },
			Position = new Vector3(0.0f, -0.5f, 0.0f),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.56f, 0.46f, 0.4f) },
		});
	}
}
