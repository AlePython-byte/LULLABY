using Godot;

/// <summary>
/// Tres deslizadores simples, «Volumen general», «Efectos» y «Música» (de 0 a 100 en pasos de 5), para el menú principal y el de pausa.
/// Leen los valores de GameSettings, los aplican a los buses con AudioManager y se guardan en user://settings.cfg.
/// </summary>
public partial class VolumeControls : VBoxContainer
{
	public HSlider MasterSlider { get; private set; }
	public HSlider SfxSlider { get; private set; }
	public HSlider MusicSlider { get; private set; }

	private Label _masterValue;
	private Label _sfxValue;
	private Label _musicValue;

	public override void _Ready()
	{
		GetNode<Label>("MasterRow/Name").Text = GameText.MasterVolume;
		GetNode<Label>("SfxRow/Name").Text = GameText.SfxVolume;
		GetNode<Label>("MusicRow/Name").Text = GameText.MusicVolume;
		MasterSlider = GetNode<HSlider>("MasterRow/Slider");
		SfxSlider = GetNode<HSlider>("SfxRow/Slider");
		MusicSlider = GetNode<HSlider>("MusicRow/Slider");
		_masterValue = GetNode<Label>("MasterRow/Value");
		_sfxValue = GetNode<Label>("SfxRow/Value");
		_musicValue = GetNode<Label>("MusicRow/Value");

		MasterSlider.SetValueNoSignal(Mathf.Round(GameSettings.MasterVolume * 100.0f));
		SfxSlider.SetValueNoSignal(Mathf.Round(GameSettings.SfxVolume * 100.0f));
		MusicSlider.SetValueNoSignal(Mathf.Round(GameSettings.MusicVolume * 100.0f));
		ShowValues();
		MasterSlider.ValueChanged += value =>
		{
			AudioManager.SetMasterVolume((float)value / 100.0f);
			ShowValues();
		};
		SfxSlider.ValueChanged += value =>
		{
			AudioManager.SetSfxVolume((float)value / 100.0f);
			ShowValues();
		};
		MusicSlider.ValueChanged += value =>
		{
			AudioManager.SetMusicVolume((float)value / 100.0f);
			ShowValues();
		};
	}

	private void ShowValues()
	{
		_masterValue.Text = $"{MasterSlider.Value:0}%";
		_sfxValue.Text = $"{SfxSlider.Value:0}%";
		_musicValue.Text = $"{MusicSlider.Value:0}%";
	}
}
