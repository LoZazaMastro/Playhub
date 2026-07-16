using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace GamingMode;

public static class L
{
	private static readonly Dictionary<string, Dictionary<string, string>> Strings = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
	{
		["en"] = new Dictionary<string, string>
		{
			["app.title"] = "Gaming Mode",
			["action.gaming"] = "Switch to Gaming Mode",
			["action.desktop"] = "Switch to Desktop Mode",
			["default.startup"] = "Default startup",
			["desktop.mode"] = "Desktop Mode",
			["gaming.mode"] = "Gaming Mode",
			["config"] = "Config",
			["splash.logo"] = "Startup logo",
			["agent.starting"] = "Starting local agent...",
			["agent.ready"] = "Agent ready.",
			["agent.unreachable"] = "Agent is not reachable on localhost:47991.",
			["status.current"] = "Current",
			["status.default"] = "Default",
			["status.shell"] = "Shell",
			["status.ready"] = "Ready.",
			["status.noStatus"] = "Agent returned no status.",
			["error.unreachable"] = "Agent is not reachable on localhost:47991.",
			["error.unreadable"] = "Agent response could not be read."
		},
		["it"] = new Dictionary<string, string>
		{
			["app.title"] = "Gaming Mode",
			["action.gaming"] = "Passa alla modalità Gaming",
			["action.desktop"] = "Passa alla modalità Desktop",
			["default.startup"] = "Avvio predefinito",
			["desktop.mode"] = "Modalità Desktop",
			["gaming.mode"] = "Modalità Gaming",
			["config"] = "Config",
			["splash.logo"] = "Logo di avvio",
			["agent.starting"] = "Avvio dell'agent locale...",
			["agent.ready"] = "Agent pronto.",
			["agent.unreachable"] = "Agent non raggiungibile su localhost:47991.",
			["status.current"] = "Attuale",
			["status.default"] = "Predefinita",
			["status.shell"] = "Shell",
			["status.ready"] = "Pronto.",
			["status.noStatus"] = "L'agent non ha restituito uno stato.",
			["error.unreachable"] = "Agent non raggiungibile su localhost:47991.",
			["error.unreadable"] = "La risposta dell'agent non può essere letta."
		},
		["es"] = new Dictionary<string, string>
		{
			["app.title"] = "Gaming Mode",
			["action.gaming"] = "Cambiar al modo Gaming",
			["action.desktop"] = "Cambiar al modo Escritorio",
			["default.startup"] = "Inicio predeterminado",
			["desktop.mode"] = "Modo Escritorio",
			["gaming.mode"] = "Modo Gaming",
			["config"] = "Config",
			["splash.logo"] = "Logo de inicio",
			["agent.starting"] = "Iniciando agente local...",
			["agent.ready"] = "Agente listo.",
			["agent.unreachable"] = "No se puede contactar con el agente en localhost:47991.",
			["status.current"] = "Actual",
			["status.default"] = "Predeterminado",
			["status.shell"] = "Shell",
			["status.ready"] = "Listo.",
			["status.noStatus"] = "El agente no devolvió ningún estado.",
			["error.unreachable"] = "No se puede contactar con el agente en localhost:47991.",
			["error.unreadable"] = "No se pudo leer la respuesta del agente."
		},
		["fr"] = new Dictionary<string, string>
		{
			["app.title"] = "Gaming Mode",
			["action.gaming"] = "Passer en mode Gaming",
			["action.desktop"] = "Passer en mode Bureau",
			["default.startup"] = "Démarrage par défaut",
			["desktop.mode"] = "Mode Bureau",
			["gaming.mode"] = "Mode Gaming",
			["config"] = "Config",
			["splash.logo"] = "Logo de démarrage",
			["agent.starting"] = "Démarrage de l'agent local...",
			["agent.ready"] = "Agent prêt.",
			["agent.unreachable"] = "Agent injoignable sur localhost:47991.",
			["status.current"] = "Actuel",
			["status.default"] = "Défaut",
			["status.shell"] = "Shell",
			["status.ready"] = "Prêt.",
			["status.noStatus"] = "L'agent n'a renvoyé aucun état.",
			["error.unreachable"] = "Agent injoignable sur localhost:47991.",
			["error.unreadable"] = "La réponse de l'agent ne peut pas être lue."
		},
		["de"] = new Dictionary<string, string>
		{
			["app.title"] = "Gaming Mode",
			["action.gaming"] = "In den Gaming-Modus wechseln",
			["action.desktop"] = "In den Desktop-Modus wechseln",
			["default.startup"] = "Standardstart",
			["desktop.mode"] = "Desktop-Modus",
			["gaming.mode"] = "Gaming-Modus",
			["config"] = "Config",
			["splash.logo"] = "Startlogo",
			["agent.starting"] = "Lokalen Agent starten...",
			["agent.ready"] = "Agent bereit.",
			["agent.unreachable"] = "Agent ist auf localhost:47991 nicht erreichbar.",
			["status.current"] = "Aktuell",
			["status.default"] = "Standard",
			["status.shell"] = "Shell",
			["status.ready"] = "Bereit.",
			["status.noStatus"] = "Agent hat keinen Status geliefert.",
			["error.unreachable"] = "Agent ist auf localhost:47991 nicht erreichbar.",
			["error.unreadable"] = "Agent-Antwort konnte nicht gelesen werden."
		},
		["pt"] = new Dictionary<string, string>
		{
			["app.title"] = "Gaming Mode",
			["action.gaming"] = "Mudar para o modo Gaming",
			["action.desktop"] = "Mudar para o modo Desktop",
			["default.startup"] = "Arranque predefinido",
			["desktop.mode"] = "Modo Desktop",
			["gaming.mode"] = "Modo Gaming",
			["config"] = "Config",
			["splash.logo"] = "Logótipo de arranque",
			["agent.starting"] = "A iniciar agente local...",
			["agent.ready"] = "Agente pronto.",
			["agent.unreachable"] = "Agente indisponível em localhost:47991.",
			["status.current"] = "Atual",
			["status.default"] = "Predefinido",
			["status.shell"] = "Shell",
			["status.ready"] = "Pronto.",
			["status.noStatus"] = "O agente não devolveu estado.",
			["error.unreachable"] = "Agente indisponível em localhost:47991.",
			["error.unreadable"] = "Não foi possível ler a resposta do agente."
		}
	};

	public static string T(string key)
	{
		foreach (string languageCandidate in GetLanguageCandidates())
		{
			if (Strings.TryGetValue(languageCandidate, out Dictionary<string, string> value) && value.TryGetValue(key, out var value2))
			{
				return value2;
			}
		}
		if (!Strings["en"].TryGetValue(key, out string value3))
		{
			return key;
		}
		return value3;
	}

	private static IEnumerable<string> GetLanguageCandidates()
	{
		string configuredLanguage = ReadConfiguredLanguage();
		if (!string.IsNullOrWhiteSpace(configuredLanguage))
		{
			yield return configuredLanguage;
			yield return configuredLanguage.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
		}
		CultureInfo currentUICulture = CultureInfo.CurrentUICulture;
		if (!string.IsNullOrWhiteSpace(currentUICulture.TwoLetterISOLanguageName))
		{
			yield return currentUICulture.TwoLetterISOLanguageName;
		}
		yield return "en";
	}

	private static string? ReadConfiguredLanguage()
	{
		try
		{
			string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode", "config.json");
			if (!File.Exists(path))
			{
				return null;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
			if (jsonDocument.RootElement.TryGetProperty("language", out var value) && value.ValueKind == JsonValueKind.String)
			{
				return value.GetString();
			}
		}
		catch
		{
		}
		return null;
	}
}
