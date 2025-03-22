using System.Text.Json;

namespace P2PViaUDP;

public static class IConfigExtensions
{
	public static string FromFile(this IConfig config, string path)
	{
		return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
	}

	public static bool ToFile(this IConfig config, string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		if (File.Exists(path))
		{
			System.IO.File.Delete(path);
		}

		File.WriteAllText(path,
			JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
		return true;
	}
}