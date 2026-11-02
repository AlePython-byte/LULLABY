using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Ejecuta el precalentamiento de la ambientación (DecorWarmup) mientras el jugador está en el menú, repartido entre fotogramas para que
/// el menú no se resienta:
///   1. Tras un instante (StartDelay), pone en cola las texturas y los modelos.
///   2. Carga y prepara unos pocos por fotograma, en el hilo principal y con un presupuesto de tiempo (DecorWarmup.Step).
///   3. Dibuja una vez, en un SubViewport propio que nadie ve, una variante de cada material y cada modelo con una luz direccional con
///      sombra y una omni, como en las salas: así las texturas y las variantes de dibujo ya están preparadas cuando se entra a la primera
///      sala. Después libera ese SubViewport.
/// Si el menú se cierra antes de terminar, el nodo desaparece con él y la siguiente vez continúa donde quedó (todo es idempotente). No
/// hace nada si ya se terminó (DecorWarmup.Done) o si DecorWarmup.Enabled es false.
/// </summary>
public partial class DecorWarmupRunner : Node
{
	public const string NodeName = "DecorWarmup";

	/// <summary>Segundos de espera antes de empezar, para que el menú aparezca sin competir con las cargas.</summary>
	[Export] public float StartDelay { get; set; } = 0.25f;

	/// <summary>Milisegundos por fotograma que se dedican a cargar (siempre se hace al menos un elemento).</summary>
	[Export] public double FrameBudgetMs { get; set; } = DecorWarmup.FrameBudgetMs;

	/// <summary>Fotogramas que se dibuja la escena de calentamiento antes de liberarla.</summary>
	[Export] public int GpuFrames { get; set; } = 3;

	public bool Finished { get; private set; }
	public event Action Completed;

	private enum Stage { Delay, Loading, Drawing }

	private Stage _stage = Stage.Delay;
	private float _elapsed;
	private int _drawn;
	private SubViewport _viewport;

	/// <summary>Cuelga un runner de parent si hace falta (devuelve null si ya está todo hecho o el precalentamiento está apagado).</summary>
	public static DecorWarmupRunner Attach(Node parent)
	{
		if (!DecorWarmup.Enabled || DecorWarmup.Done)
		{
			return null;
		}

		DecorWarmupRunner runner = new() { Name = NodeName };
		parent.AddChild(runner);
		return runner;
	}

	public override void _Ready()
	{
		DecorWarmup.Request();
	}

	public override void _Process(double delta)
	{
		switch (_stage)
		{
			case Stage.Delay:
				_elapsed += (float)delta;
				if (_elapsed >= StartDelay)
				{
					_stage = Stage.Loading;
				}

				break;
			case Stage.Loading:
				if (!DecorWarmup.Step(FrameBudgetMs))
				{
					BuildDrawing();
					_stage = Stage.Drawing;
				}

				break;
			case Stage.Drawing:
				if (++_drawn >= GpuFrames)
				{
					Finish();
				}

				break;
		}
	}

	private void Finish()
	{
		_viewport?.QueueFree();
		_viewport = null;
		DecorWarmup.MarkDone();
		Finished = true;
		Completed?.Invoke();
		SetProcess(false);
		QueueFree();
	}

	// Escena mínima con una cuadrícula de objetos a la vista de una cámara ortogonal: cada textura cargada con el material triplanar de las
	// salas, uno de solo color y cada modelo de la decoración reducido a medio metro.
	private void BuildDrawing()
	{
		_viewport = new SubViewport
		{
			Name = "DecorWarmupViewport",
			Size = new Vector2I(256, 256),
			OwnWorld3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		AddChild(_viewport);

		_viewport.AddChild(new Camera3D
		{
			Projection = Camera3D.ProjectionType.Orthogonal,
			Size = 14.0f,
			Current = true,
			Transform = new Transform3D(Basis.LookingAt(new Vector3(0.0f, -1.0f, -0.35f), Vector3.Up), new Vector3(0.0f, 10.0f, 3.5f)),
		});
		_viewport.AddChild(new DirectionalLight3D { ShadowEnabled = true, Transform = new Transform3D(Basis.LookingAt(new Vector3(-0.3f, -1.0f, -0.4f), Vector3.Up), Vector3.Zero) });
		_viewport.AddChild(new OmniLight3D { Position = new Vector3(0.0f, 3.0f, 0.0f), OmniRange = 12.0f });

		List<Node3D> items = new();
		foreach (Texture2D texture in DecorWarmup.Held.OfType<Texture2D>())
		{
			items.Add(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.2f, 0.6f) }, MaterialOverride = RoomSurfaces.Textured(texture, Colors.White, 4.0f) });
		}

		items.Add(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.2f, 0.6f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.56f, 0.46f, 0.4f) } });
		foreach (string path in DecorWarmup.ModelPaths().Where(path => ResourceLoader.Exists(path)))
		{
			Node3D model = ModelLibrary.Create(path, ModelLibrary.Anchor.BaseCenter, 0.5f);
			if (model != null)
			{
				items.Add(model);
			}
		}

		for (int i = 0; i < items.Count; i++)
		{
			items[i].Position = new Vector3(-6.0f + (i % 8) * 1.6f, 0.0f, -3.0f + (i / 8) * 1.6f);
			_viewport.AddChild(items[i]);
		}
	}
}
