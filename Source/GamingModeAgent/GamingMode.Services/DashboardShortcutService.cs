using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using GamingMode.Models;

namespace GamingMode.Services;

// LA PARTE NATIVA DELLA PLAYHUB DASHBOARD.
//
// LEGGERE PRIMA DI AGGIUNGERE QUALSIASI COSA QUI DENTRO.
//
// Questo servizio NON tocca i controller. Non li legge, non li apre, non li
// configura, non installa nulla che intercetti il loro input. E' una regola,
// non una preferenza, e viene da un errore pagato caro.
//
// Cosa era successo. Per far navigare una Dashboard con il pad avevo messo SDL
// nell'agente. SDL non si limita ad ascoltare: per Xbox, PlayStation e Steam
// Controller usa driver HIDAPI dedicati, attivi in modo predefinito, che aprono
// il dispositivo e ne cambiano la configurazione. Steam Input sta facendo la
// stessa cosa sullo stesso firmware. Risultato: comandi sdoppiati, tasto Steam
// impazzito, la Home laterale che non si apriva piu'. L'unico pad che
// funzionava era quello generico, che SDL lascia in pace.
//
// La regola che ne discende: il controller e' di Steam. Quando l'interfaccia
// di Steam e' in primo piano, e' Steam a far navigare la Dashboard, perche' la
// Dashboard e' una sua schermata. Noi non entriamo mai nel mezzo.
//
// Resta quindi una sola cosa:
//
//   1. UNA SCORCIATOIA DA TASTIERA, registrata con RegisterHotKey. Serve
//      perche' mentre un gioco e' in primo piano l'interfaccia di Steam non
//      riceve nulla, quindi nessun codice che vive nel plugin puo' accorgersi
//      di niente. Chi vuole aprirla con il pad mentre gioca lega la stessa
//      combinazione a un accordo DENTRO Steam: e' Steam a premere i tasti, e
//      noi non tocchiamo il dispositivo.
//
// Quando la scorciatoia scatta, qui non si apre niente: si alza una bandierina
// che il plugin raccoglie. Nessuna finestra, nessun cambio di primo piano,
// nessun messaggio spedito a Steam.
public sealed class DashboardShortcutService : IDisposable
{
	private const int HotkeyId = 0x5048;
	private const uint ModAlt = 0x0001;
	private const uint ModControl = 0x0002;
	private const uint ModShift = 0x0004;
	private const uint ModNoRepeat = 0x4000;
	private const uint WmHotKey = 0x0312;
	private const int WmQuit = 0x0012;

	private readonly JsonStore _store;
	private readonly Func<bool> _launchCurtainOnScreen;
	private readonly Func<bool>? _dashboardVisible;
	private readonly Action? _closeDashboard;
	private readonly FileLogger _logger;
	private readonly object _sync = new();

	private Thread? _hotkeyThread;
	private Thread? _uiThread;
	private CancellationTokenSource? _cancellation;
	private Dispatcher? _dispatcher;
	private uint _hotkeyThreadId;
	private int _hotkeyReloadGeneration;
	private bool _running;
	private long _openRequestedAt;
	private long _lastActivationAt;
	private long _primaryWindowHandle;

	public DashboardShortcutService(JsonStore store,
		Func<bool> launchCurtainOnScreen, FileLogger logger,
		Func<bool>? dashboardVisible = null, Action? closeDashboard = null)
	{
		_store = store;
		_launchCurtainOnScreen = launchCurtainOnScreen;
		_dashboardVisible = dashboardVisible;
		_closeDashboard = closeDashboard;
		_logger = logger;
	}

	public void Start()
	{
		lock (_sync)
		{
			if (_running) return;
			_running = true;
			_cancellation = new CancellationTokenSource();

			_uiThread = new Thread(RunUi) { IsBackground = true, Name = "Playhub Dashboard UI" };
			_uiThread.SetApartmentState(ApartmentState.STA);
			_uiThread.Start();

			_hotkeyThread = new Thread(() => RunHotkey(_cancellation.Token))
			{
				IsBackground = true,
				Name = "Playhub Dashboard hotkey"
			};
			_hotkeyThread.Start();
		}
		_logger.Info("Playhub Dashboard shortcut started. The Dashboard itself lives in the Steam plugin; no controller is touched.");
	}

	// Rilegge la combinazione dalla configurazione e la registra di nuovo.
	//
	// Serve perche' RegisterHotKey lega UNA combinazione al thread una volta
	// sola: cambiarla nella configurazione non bastava, restava buona quella
	// letta all'avvio dell'agente. Dal punto di vista di chi la cambiava, la
	// scorciatoia nuova semplicemente non funzionava.
	public void ReloadHotkey()
	{
		lock (_sync)
		{
			if (!_running || _cancellation is null) return;
			int generation = ++_hotkeyReloadGeneration;

			// Il thread esce dal suo giro di messaggi e ne parte uno nuovo, che
			// rilegge la configurazione da capo.
			if (_hotkeyThreadId != 0) PostThreadMessage(_hotkeyThreadId, WmQuit, 0, 0);

			CancellationToken token = _cancellation.Token;
			Thread replacement = new(() =>
			{
				// Un istante di respiro: il thread precedente deve prima
				// togliere la sua registrazione, altrimenti la nuova trova la
				// combinazione occupata da noi stessi.
				Thread.Sleep(150);
				if (generation != Volatile.Read(ref _hotkeyReloadGeneration) || token.IsCancellationRequested) return;
				RunHotkey(token);
			})
			{
				IsBackground = true,
				Name = "Playhub Dashboard hotkey"
			};
			_hotkeyThread = replacement;
			replacement.Start();
		}
	}

	public void Stop()
	{
		CancellationTokenSource? cancellation;
		lock (_sync)
		{
			if (!_running) return;
			_running = false;
			_hotkeyReloadGeneration++;
			cancellation = _cancellation;
			_cancellation = null;
		}
		cancellation?.Cancel();
		if (_hotkeyThreadId != 0) PostThreadMessage(_hotkeyThreadId, WmQuit, 0, 0);
		try
		{
			_dispatcher?.Invoke(() =>
			{
				Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
			}, DispatcherPriority.Send);
		}
		catch
		{
		}
		_dispatcher = null;
		_hotkeyThread = null;
		_uiThread = null;
		cancellation?.Dispose();
	}

	public void Dispose() => Stop();

	// ---------------- il ponte verso il plugin ----------------

	public bool RequestOpen(string why)
	{
		if (!ReadSettings().DashboardEnabled)
		{
			_openRequestedAt = 0;
			_logger.Info($"Playhub Dashboard open ignored ({why}); Dashboard disabled.");
			return false;
		}

		// UNA PRESSIONE PER VOLTA.
		//
		// La combinazione si preme volentieri due o tre volte quando sembra non
		// rispondere. Ogni pressione faceva partire un altro tentativo di
		// attivazione, e le attivazioni a raffica sono il modo piu' rapido di
		// disturbare Steam e chiunque altro sia a schermo. Entro un secondo e
		// mezzo la richiesta e' gia' in corso: si lascia perdere.
		long now = Environment.TickCount64;
		if (_lastActivationAt != 0 && now - _lastActivationAt < 1500)
		{
			return false;
		}

		_lastActivationAt = now;
		Interlocked.Exchange(ref _primaryWindowHandle, OverlayWindowTools.GetForegroundWindowHandle().ToInt64());
		_openRequestedAt = now;
		_logger.Info($"Playhub Dashboard open requested ({why}); the Steam plugin will show it.");

		// Do not bring Steam forward here. The foreground window is the exact
		// destination the Dashboard must restore on exit. The plugin can navigate
		// while Steam is behind; the DWM mirror service captures this HWND first,
		// then gives input ownership to Steam only when the route is ready.
		return true;
	}

	public nint PrimaryWindowHandle => new(Interlocked.Read(ref _primaryWindowHandle));

	// Il plugin interroga di continuo. La richiesta si consuma alla lettura e
	// scade da sola: una premuta vecchia non deve aprire niente piu' tardi.
	public bool ConsumeOpenRequest()
	{
		long at = _openRequestedAt;
		if (at == 0 || Environment.TickCount64 - at > 4000) return false;
		_openRequestedAt = 0;
		return true;
	}

	private void RunUi()
	{
		try
		{
			Application application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
			_dispatcher = Dispatcher.CurrentDispatcher;
			application.Run();
		}
		catch (Exception exception)
		{
			_logger.Error("Il dispatcher della Dashboard si e' interrotto.", exception);
		}
	}

	// ---------------- scegliere un programma dal disco ----------------
	//
	// La finestra "Apri" di Windows, quella vera. E' l'unico modo per prendere
	// un programma che non compare in nessun elenco - un .exe portatile, un
	// emulatore scompattato in una cartella - e la strada che la Dashboard
	// aveva prima.
	//
	// L'accortezza che serve: la finestra deve stare SOPRA la Big Picture,
	// altrimenti si apre dietro e sembra che non sia successo niente. Le si da'
	// come proprietaria una finestra invisibile sempre in primo piano, e quando
	// si chiude si rimanda avanti Steam.
	public string PickProgramFile()
	{
		Dispatcher? dispatcher = _dispatcher;
		if (dispatcher is null)
		{
			_logger.Error("Playhub Dashboard file picker unavailable.", new InvalidOperationException("no UI dispatcher"));
			return "";
		}

		string chosen = "";
		try
		{
			dispatcher.Invoke(() =>
			{
				Window owner = new()
				{
					Width = 1,
					Height = 1,
					WindowStyle = WindowStyle.None,
					ShowInTaskbar = false,
					Topmost = true,
					Left = -2000,
					Top = -2000,
					AllowsTransparency = true,
					Background = System.Windows.Media.Brushes.Transparent
				};

				try
				{
					owner.Show();
					Microsoft.Win32.OpenFileDialog dialog = new()
					{
						Title = "Scegli un programma",
						Filter = "Programmi|*.exe;*.lnk;*.bat;*.cmd|Tutti i file|*.*",
						CheckFileExists = true
					};
					if (dialog.ShowDialog(owner) == true) chosen = dialog.FileName;
				}
				finally
				{
					owner.Close();
				}
			});
		}
		catch (Exception exception)
		{
			_logger.Error("Playhub Dashboard could not show the file picker.", exception);
		}

		// Chiusa la finestra, il primo piano va restituito a Steam: e' li' che
		// l'utente stava lavorando.
		System.Threading.Tasks.Task.Run(() =>
		{
			try { OverlayWindowTools.ActivateSteam(); } catch { }
		});

		return chosen;
	}

	// ---------------- imparare la combinazione ----------------
	//
	// PERCHE' NON LA IMPARA LA PAGINA.
	//
	// Dentro l'interfaccia di Steam un ascoltatore di tasti non riceve quasi
	// niente: Steam consuma gli eventi prima, e con il pad in mano la tastiera
	// non ha nemmeno il fuoco. Il risultato e' che "premi la combinazione" non
	// registrava mai nulla. L'agente invece la tastiera la vede eccome, ed e'
	// gia' lui a doverla riconoscere quando la scorciatoia e' in funzione: e'
	// giusto che sia lui a impararla.
	//
	// L'ascolto dura pochi secondi, sta su un thread suo che nasce e muore con
	// la richiesta, e riguarda SOLO la tastiera. Nessun controller viene aperto.

	private volatile string _learnState = "idle";
	private volatile string _learnCombo = "";
	private readonly object _learnLock = new();

	// L'esito si consuma alla lettura. Se restasse scritto, la volta successiva
	// che qualcuno guarda troverebbe subito il risultato della volta prima e
	// scriverebbe una combinazione che nessuno ha appena premuto.
	public (string State, string Combo) ReadLearnState()
	{
		lock (_learnLock)
		{
			string state = _learnState;
			string combo = _learnCombo;
			if (state is "done" or "cancelled" or "timeout" or "failed")
			{
				_learnState = "idle";
				_learnCombo = "";
			}
			return (state, combo);
		}
	}

	public void BeginLearn(int secondsToWait = 10)
	{
		lock (_learnLock)
		{
			if (_learnState == "waiting") return;
			_learnState = "waiting";
			_learnCombo = "";
		}

		Thread worker = new(() => RunLearn(secondsToWait)) { IsBackground = true, Name = "PlayhubDashboardLearn" };
		worker.Start();
	}

	private void RunLearn(int secondsToWait)
	{
		nint hook = 0;
		try
		{
			// Il gancio va installato dal thread che poi smaltisce i messaggi:
			// e' questo, e vive solo per la durata dell'ascolto.
			_learnHookCallback = LearnHookProc;

			// L'ultimo argomento vuole il modulo che contiene la procedura.
			// Passare zero e' documentato come ammissibile per i ganci di basso
			// livello, ma su .NET moderno viene rifiutato con "hook needs hmod":
			// era questo a far fallire l'ascolto in silenzio, e quindi la
			// combinazione non cambiava mai.
			nint module = GetModuleHandle(null);
			hook = SetWindowsHookEx(WhKeyboardLowLevel, _learnHookCallback, module, 0);
			if (hook == 0)
			{
				int error = Marshal.GetLastWin32Error();
				lock (_learnLock) { _learnState = "failed"; }
				_logger.Error("Playhub Dashboard could not listen for the shortcut.",
					new InvalidOperationException($"SetWindowsHookEx failed, error {error}"));
				return;
			}

			_logger.Info("Playhub Dashboard is listening for a new shortcut.");

			long deadline = Environment.TickCount64 + secondsToWait * 1000L;
			while (Environment.TickCount64 < deadline)
			{
				lock (_learnLock)
				{
					if (_learnState != "waiting") break;
				}

				// Un gancio di basso livello viene servito dalla coda dei
				// messaggi: senza questo giro non verrebbe mai chiamato.
				while (PeekMessage(out NativeMessage message, 0, 0, 0, 1))
				{
					TranslateMessage(ref message);
					DispatchMessage(ref message);
				}
				Thread.Sleep(15);
			}

			lock (_learnLock)
			{
				if (_learnState == "waiting")
				{
					_learnState = "timeout";
				}
			}
		}
		catch (Exception exception)
		{
			lock (_learnLock) { _learnState = "failed"; }
			_logger.Error("Playhub Dashboard could not learn the shortcut.", exception);
		}
		finally
		{
			if (hook != 0) UnhookWindowsHookEx(hook);
			_learnHookCallback = null;
		}
	}

	private HookProc? _learnHookCallback;

	private nint LearnHookProc(int code, nint wParam, nint lParam)
	{
		if (code < 0) return CallNextHookEx(0, code, wParam, lParam);

		try
		{
			bool down = wParam == WmKeyDown || wParam == WmSysKeyDown;
			if (!down) return CallNextHookEx(0, code, wParam, lParam);

			uint key = (uint)Marshal.ReadInt32(lParam);

			// Esc annulla; i modificatori da soli non sono una combinazione.
			if (key == 0x1B)
			{
				lock (_learnLock) { _learnState = "cancelled"; }
				return 1;
			}
			if (IsModifierKey(key)) return CallNextHookEx(0, code, wParam, lParam);

			List<string> parts = new();
			if ((GetAsyncKeyState(0x11) & 0x8000) != 0) parts.Add("Ctrl");
			if ((GetAsyncKeyState(0x12) & 0x8000) != 0) parts.Add("Alt");
			if ((GetAsyncKeyState(0x10) & 0x8000) != 0) parts.Add("Shift");

			if (parts.Count == 0)
			{
				// Un tasto senza modificatori verrebbe rubato a chiunque stia
				// scrivendo. Si lascia passare e si continua ad aspettare.
				return CallNextHookEx(0, code, wParam, lParam);
			}

			string name = HotkeyParser.DescribeKey(key);
			if (string.IsNullOrEmpty(name)) return CallNextHookEx(0, code, wParam, lParam);

			parts.Add(name);
			string combo = string.Join("+", parts);

			// Si accetta solo se e' davvero registrabile: meglio scoprirlo ora
			// che ritrovarsi una scorciatoia scritta nella configurazione e
			// muta.
			if (!HotkeyParser.TryParse(combo, out _, out _)) return CallNextHookEx(0, code, wParam, lParam);

			lock (_learnLock)
			{
				_learnCombo = combo;
				_learnState = "done";
			}
			_logger.Info($"Playhub Dashboard learned the shortcut: {combo}.");

			// Il tasto non deve arrivare a Steam: l'utente stava configurando,
			// non giocando.
			return 1;
		}
		catch
		{
			return CallNextHookEx(0, code, wParam, lParam);
		}
	}

	private static bool IsModifierKey(uint key) =>
		key is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

	private const int WhKeyboardLowLevel = 13;
	private const nint WmKeyDown = 0x0100;
	private const nint WmSysKeyDown = 0x0104;

	private delegate nint HookProc(int code, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint GetModuleHandle(string? name);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnhookWindowsHookEx(nint hook);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int key);

	[DllImport("user32.dll")]
	private static extern bool PeekMessage(out NativeMessage message, nint window, uint filterMin, uint filterMax, uint remove);

	[DllImport("user32.dll")]
	private static extern bool TranslateMessage(ref NativeMessage message);

	[DllImport("user32.dll")]
	private static extern nint DispatchMessage(ref NativeMessage message);

	// ---------------- scorciatoia da tastiera ----------------

	private void RunHotkey(CancellationToken token)
	{
		_hotkeyThreadId = GetCurrentThreadId();
		bool registered = false;
		string description = "";
		try
		{
			GamingSettings settings = ReadSettings();
			if (settings.DashboardKeyboardShortcutEnabled
				&& HotkeyParser.TryParse(settings.DashboardHotkey, out uint modifiers, out uint key))
			{
				description = settings.DashboardHotkey;
				registered = RegisterHotKey(0, HotkeyId, modifiers | ModNoRepeat, key);
				_logger.Info(registered
					? $"Playhub Dashboard hotkey registered: {description}."
					: $"Playhub Dashboard hotkey '{description}' is already taken by another program.");
			}
			else
			{
				_logger.Info("Playhub Dashboard keyboard shortcut disabled.");
			}

			while (!token.IsCancellationRequested && GetMessage(out NativeMessage message, 0, 0, 0) > 0)
			{
				if (message.Value != WmHotKey || message.WParam != HotkeyId) continue;
				if (_dashboardVisible?.Invoke() == true)
				{
					_logger.Info($"Playhub Dashboard close requested ({description}); emergency toggle.");
					_closeDashboard?.Invoke();
				}
				else
				{
					RequestOpen($"scorciatoia da tastiera ({description})");
				}
			}
		}
		catch (Exception exception)
		{
			_logger.Error("La scorciatoia da tastiera si e' interrotta.", exception);
		}
		finally
		{
			if (registered) UnregisterHotKey(0, HotkeyId);
			_hotkeyThreadId = 0;
		}
	}

	private GamingSettings ReadSettings()
	{
		try { return _store.LoadConfig().Gaming; }
		catch { return new GamingSettings(); }
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeMessage
	{
		public nint Handle;
		public uint Value;
		public nint WParam;
		public nint LParam;
		public uint Time;
		public int PointX;
		public int PointY;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnregisterHotKey(nint window, int id);

	[DllImport("user32.dll")]
	private static extern int GetMessage(out NativeMessage message, nint window, uint min, uint max);

	[DllImport("user32.dll")]
	private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	// LETTURA DELLA COMBINAZIONE DI TASTI.
	//
	// Modificatori ammessi: Ctrl, Alt, Shift. Niente tasto Windows, riservato al
	// sistema. Il tasto finale puo' essere una lettera, una cifra, un tasto
	// funzione, un tasto di servizio o un segno di punteggiatura.
	//
	// I segni si chiedono alla DISPOSIZIONE ATTIVA, non a una tabella scritta a
	// mano: il tasto che produce "u con accento" non ha un codice fisso, dipende
	// dalla tastiera. Cosi' la stessa impostazione vale su qualunque layout.
	internal static class HotkeyParser
	{
		private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
		{
			["PAGSU"] = 0x21, ["PAG SU"] = 0x21, ["PAGEUP"] = 0x21, ["PGUP"] = 0x21,
			["PAGGIU"] = 0x22, ["PAG GIU"] = 0x22, ["PAGEDOWN"] = 0x22, ["PGDN"] = 0x22,
			["FINE"] = 0x23, ["END"] = 0x23,
			["INIZIO"] = 0x24, ["HOME"] = 0x24,
			["SINISTRA"] = 0x25, ["LEFT"] = 0x25,
			["SU"] = 0x26, ["UP"] = 0x26,
			["DESTRA"] = 0x27, ["RIGHT"] = 0x27,
			["GIU"] = 0x28, ["DOWN"] = 0x28,
			["STAMP"] = 0x2C, ["PRINTSCREEN"] = 0x2C,
			["INS"] = 0x2D, ["INSERT"] = 0x2D,
			["CANC"] = 0x2E, ["DELETE"] = 0x2E, ["DEL"] = 0x2E,
			["INVIO"] = 0x0D, ["ENTER"] = 0x0D, ["RETURN"] = 0x0D,
			["ESC"] = 0x1B, ["ESCAPE"] = 0x1B,
			["SPAZIO"] = 0x20, ["SPACE"] = 0x20,
			["TAB"] = 0x09,
			["BACKSPACE"] = 0x08,
			["PAUSA"] = 0x13, ["PAUSE"] = 0x13
		};

		// Il nome da scrivere nella configurazione per un tasto appena premuto.
		// Deve essere un nome che TryParse sa rileggere: si usa la stessa
		// tabella, percorsa al contrario, poi le funzioni, poi il carattere che
		// quel tasto produce con la disposizione attiva. Cosi' una tastiera
		// italiana scrive "ù" e una inglese scrive ";" senza che nessuno debba
		// sapere quale sia.
		public static string DescribeKey(uint key)
		{
			if (key is >= 0x70 and <= 0x87) return "F" + (key - 0x70 + 1);

			foreach (KeyValuePair<string, uint> pair in Named)
			{
				// Il primo nome di ogni gruppo e' quello italiano: e' quello che
				// vogliamo mostrare.
				if (pair.Value == key) return pair.Key;
			}

			if (key is >= 0x30 and <= 0x39) return ((char)key).ToString();
			if (key is >= 0x41 and <= 0x5A) return ((char)key).ToString();

			uint character = MapVirtualKeyEx(key, MapvkVkToChar, ActiveLayout());
			if (character == 0) return "";

			char produced = (char)(character & 0x7FFF);
			return char.IsControl(produced) ? "" : produced.ToString();
		}

		private const uint MapvkVkToChar = 2;

		[DllImport("user32.dll")]
		private static extern uint MapVirtualKeyEx(uint code, uint mapType, nint layout);

		public static bool TryParse(string text, out uint modifiers, out uint key)
		{
			modifiers = 0;
			key = 0;
			if (string.IsNullOrWhiteSpace(text)) return false;
			foreach (string rawPart in Split(text))
			{
				string part = rawPart.Trim();
				if (part.Length == 0) continue;
				switch (part.ToUpperInvariant())
				{
					case "CTRL":
					case "CONTROL":
						modifiers |= ModControl;
						continue;
					case "ALT":
						modifiers |= ModAlt;
						continue;
					case "SHIFT":
					case "MAIUSC":
						modifiers |= ModShift;
						continue;
				}
				if (key != 0) return false;
				if (Named.TryGetValue(part, out uint named))
				{
					key = named;
					continue;
				}
				if (part.Length >= 2 && (part[0] is 'F' or 'f')
					&& int.TryParse(part[1..], out int functionKey)
					&& functionKey is >= 1 and <= 24)
				{
					key = (uint)(0x70 + functionKey - 1);
					continue;
				}
				if (part.Length != 1) return false;
				char character = part[0];
				if (char.IsLetterOrDigit(character) && character < 128)
				{
					key = char.ToUpperInvariant(character);
					continue;
				}
				short scan = VkKeyScanEx(character, ActiveLayout());
				if (scan == -1) return false;
				key = (uint)(scan & 0xFF);
				if (key == 0) return false;
			}
			// Un tasto senza modificatori verrebbe rubato a chiunque stia
			// scrivendo: si rifiuta di proposito.
			return key != 0 && modifiers != 0;
		}

		// Il "+" e' anche un tasto: se e' l'ultimo pezzo va inteso come tasto.
		private static List<string> Split(string text)
		{
			List<string> parts = new();
			int start = 0;
			for (int i = 0; i < text.Length; i++)
			{
				if (text[i] != '+') continue;
				if (i == text.Length - 1 && i > start) break;
				parts.Add(text[start..i]);
				start = i + 1;
			}
			if (start < text.Length) parts.Add(text[start..]);
			return parts;
		}

		private static nint ActiveLayout()
		{
			try
			{
				nint foreground = GetForegroundWindow();
				uint thread = foreground != 0 ? GetWindowThreadProcessId(foreground, out _) : 0u;
				return GetKeyboardLayout(thread);
			}
			catch
			{
				return GetKeyboardLayout(0);
			}
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern short VkKeyScanEx(char character, nint layout);

		[DllImport("user32.dll")]
		private static extern nint GetKeyboardLayout(uint threadId);

		[DllImport("user32.dll")]
		private static extern nint GetForegroundWindow();

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
	}
}
