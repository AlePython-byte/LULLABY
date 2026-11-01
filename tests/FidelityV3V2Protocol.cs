using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

/// <summary>Lector aislado del protocolo v2 y comprobacion del nucleo medido en desarrollo.</summary>
public sealed class FidelityV3V2Protocol : IDisposable
{
	public const string ProtocolPath = "ai-training/protocols/v3_fidelity_v2.json";
	public const string RegisteredHash = "6cdb35ad0531dc2c5fe48b835d41052bd8288fe228f4f793fbbcd51ce93dc5f1";
	private readonly JsonDocument _document;
	public JsonElement Root => _document.RootElement;
	public int Runs => Root.GetProperty("runs_per_level_per_environment").GetInt32();
	public float MaxSeconds => Root.GetProperty("max_seconds_per_run").GetSingle();

	public FidelityV3V2Protocol()
	{
		if (FidelityV3Protocol.HashFile(ProtocolPath) != RegisteredHash)
			throw new InvalidDataException("Protocolo v2 distinto del congelado.");
		_document = JsonDocument.Parse(File.ReadAllBytes(ProtocolPath));
		if (Root.GetProperty("protocol_version").GetInt32() != 2 || Runs != 10
			|| Root.GetProperty("generator_version").GetInt32() != 3
			|| Root.GetProperty("simulator_version").GetInt32() != LevelSimulatorV3.Version
			|| BotBrainV3.PolicyVersion != 1)
			throw new InvalidDataException("Versiones incompatibles con fidelidad v2.");
		using FidelityV3Protocol original = new();
	}

	public (int First, int Count) Sample(string mode)
	{
		if (mode == "preflight") return (600001, 1);
		if (mode != "acceptance" && mode != "reserve") throw new ArgumentException("Modo no admitido.");
		JsonElement sample = Root.GetProperty("samples").GetProperty(mode);
		return (sample.GetProperty("first_seed").GetInt32(), sample.GetProperty("count").GetInt32());
	}

	public static string CheckAuthorization(string path, string mode)
	{
		if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Aceptacion/reserva requieren autorizacion externa; el preflight no la concede.");
		using JsonDocument authorization = JsonDocument.Parse(File.ReadAllBytes(path));
		JsonElement value = authorization.RootElement;
		if (value.GetProperty("mode").GetString() != mode
			|| value.GetProperty("protocol_sha256").GetString() != RegisteredHash
			|| !value.GetProperty("user_approved").GetBoolean()
			|| !value.GetProperty("machine_confirmed_free").GetBoolean()
			|| string.IsNullOrWhiteSpace(value.GetProperty("approved_at_utc").GetString()))
			throw new InvalidDataException("La autorizacion externa no corresponde a este protocolo, modo o disponibilidad.");
		return FidelityV3Protocol.HashFile(path);
	}

	public SortedDictionary<string, string> Fingerprints(string authorizationPath)
	{
		var hashes = FidelityV3Protocol.Fingerprints();
		hashes[ProtocolPath] = FidelityV3Protocol.HashFile(ProtocolPath);
		foreach (string path in new[] { "tests/FidelityV3V2Check.tscn", "ai-training/validation/fidelity_v2.py", "ai-training/validation/run_fidelity_v2.py",
			"ai-training/protocols/v3_repetition_curve_plan.json", "ai-training/validation/repetition_curve_v3.py" })
			hashes[path] = FidelityV3Protocol.HashFile(path);
		if (!string.IsNullOrEmpty(authorizationPath)) hashes["authorization_file"] = FidelityV3Protocol.HashFile(authorizationPath);
		return hashes;
	}

	public void AssertMeasurementCore(SortedDictionary<string, string> current)
	{
		JsonElement reference = Root.GetProperty("freeze_contract");
		string archivePath = reference.GetProperty("measurement_reference_archive").GetString();
		string expectedArchive = Root.GetProperty("registration").GetProperty("development_evidence").GetProperty("raw_archive_sha256").GetString();
		if (FidelityV3Protocol.HashFile(archivePath) != expectedArchive) throw new InvalidDataException("Archivo de referencia alterado.");
		using ZipArchive archive = ZipFile.OpenRead(archivePath);
		using Stream stream = archive.GetEntry(reference.GetProperty("measurement_reference_manifest").GetString()).Open();
		using MemoryStream memory = new();
		stream.CopyTo(memory);
		if (FidelityV3Protocol.HashBytes(memory.ToArray()) != reference.GetProperty("measurement_reference_manifest_sha256").GetString())
			throw new InvalidDataException("Manifiesto de referencia alterado.");
		using JsonDocument manifest = JsonDocument.Parse(memory.ToArray());
		foreach (var property in manifest.RootElement.GetProperty("fingerprints").EnumerateObject())
		{
			// Los arneses nuevos y su ensamblado cambian; el juego, bot, simulador y PlayReal originales no.
			if (property.Name == "executed_assembly" || property.Name == "authorization_file") continue;
			if (!current.TryGetValue(property.Name, out string hash) || hash != property.Value.GetString())
				throw new InvalidDataException("Nucleo o archivo historico modificado: " + property.Name);
		}
	}

	public void AssertFrozen(SortedDictionary<string, string> expected, string authorizationPath)
	{
		if (!expected.SequenceEqual(Fingerprints(authorizationPath))) throw new InvalidDataException("Cambio de codigo, parametros, protocolo o ensamblado durante el lote.");
	}

	public void Dispose() => _document.Dispose();
}
