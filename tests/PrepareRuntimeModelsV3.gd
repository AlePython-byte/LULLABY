extends SceneTree

# Preparacion local de recursos para pruebas sin iniciar el editor.
func _initialize():
	var names = ["noah", "basic_enemy", "shooter_enemy", "exploder_enemy", "flyer_enemy", "runner_enemy", "digger_enemy", "the_mother", "tear", "enemy_projectile"]
	for model in names:
		var source = "res://assets/models/" + model + ".glb"
		var config = ConfigFile.new()
		if config.load(source + ".import") != OK:
			push_error("Sin metadatos: " + source)
			quit(1)
			return
		var destination = config.get_value("remap", "path", "")
		if FileAccess.file_exists(destination):
			continue
		var document = GLTFDocument.new()
		var state = GLTFState.new()
		if document.append_from_file(source, state) != OK:
			quit(1)
			return
		var scene = document.generate_scene(state)
		var packed = PackedScene.new()
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(destination.get_base_dir()))
		if packed.pack(scene) != OK or ResourceSaver.save(packed, destination) != OK:
			quit(1)
			return
		scene.free()
		print("Cache local: ", model)
	quit(0)
