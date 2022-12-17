using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

/*
 * TODO: 
 * 引数解析処理を追加する
 * 現在引数を直接処理対象にしているのを解析結果を基に処理するよう変更する
 * 設定ファイルを引数指定できるようにする
 */

namespace AutoStore {
	class Program
	{
		static readonly Settings settings = new Settings();
		static TextWriter log = new StringWriter();

		/// <summary>
		/// 初期化処理 動作パラメーターをファイル等から設定する
		/// </summary>
		static void Initialize(CommandLine cmdline)
		{
			WriteLogLine(log, "*** Initialize ***");

			// デフォルト設定はコンストラクタで設定される

			// ここから、各設定値による上書き
			// 後から参照するものほど優先順位が高い

			// 読み込み＋ログ出力
			static void ReadSettingFromIni(string path, string note) {
				if (settings.ReadIni(path, true)) {
					WriteLogLine(log, $"Setting loaded from '{path}'.{(note != null ? "(" + note + ")" : "")}");
				}
			}

			string? exeLocation = Assembly.GetExecutingAssembly()?.Location;
			if (exeLocation != null) {
				// 自身の代替ストリーム
				ReadSettingFromIni(exeLocation + ":SETTING", "Substream");
			}

			// 実行フォルダにある設定ファイル
			if (Path.GetDirectoryName(exeLocation) is string exeLocationDir) {
				ReadSettingFromIni(Path.Combine(exeLocationDir, "autostore.ini"), "Executable Directory ini file");
			}
		

			// カレントフォルダの設定ファイル
			ReadSettingFromIni(Path.Combine(System.Environment.CurrentDirectory, "autostore.ini"), "Current Directory ini file");

			// 引数で指定された設定ファイル
			if (cmdline.Options.TryGetValue("config", out string? s) && s != null) {
				if (s.StartsWith("\"") && s.EndsWith("\"")) s = s.Substring(1, s.Length - 1);
				ReadSettingFromIni(s, "CommandLine argument");
			}

			// ここまでで設定値の取得完了
			WriteLogLine(log, $"setting[{nameof(settings.BaseDirectory)}] = '{settings.BaseDirectory}'");
			WriteLogLine(log, $"setting[{nameof(settings.ArchiveDirectory)}] = '{settings.ArchiveDirectory}'");
			WriteLogLine(log, $"setting[{nameof(settings.DateTimeFormat)}] = '{settings.DateTimeFormat}'");
			WriteLogLine(log, $"setting[{nameof(settings.ExcludePatterns)}] = '{string.Join(';', settings.ExcludePatterns)}'");
			WriteLogLine(log, $"setting[{nameof(settings.MakeJunction)}] = '{settings.MakeJunction}'");

		}

		static string ReplaceParameter(string s)
		{
			// (ローカル関数)パラメーター文字列の解決処理
			// %....% 部分を置換する(%% => %)
			bool TryResolveParameter(string paramName, out string replacedValue)
			{
				
				// 先頭が「env:」「dir:」で始まっていた場合はその処理を優先する
				bool isEnv = paramName.StartsWith("env:", true, null);
				bool isDir = paramName.StartsWith("dir:", true, null);

				bool scopeSpecified = isEnv || isDir;
				if (scopeSpecified)
				{
					paramName = paramName.Substring(paramName.IndexOf(':') + 1);
				}

				// 環境変数
				if ((!scopeSpecified || isEnv) && System.Environment.GetEnvironmentVariable(paramName) is string envValue)
				{
					replacedValue = envValue;
					return true;
				}

				// システムディレクトリ名
				// Environment.SpecialFolder 列挙の名前と合致する場合に実施
				if ((!scopeSpecified || isDir) && Enum.TryParse<Environment.SpecialFolder>(paramName, true, out var specialFolder))
				{
					replacedValue = System.Environment.GetFolderPath(specialFolder, Environment.SpecialFolderOption.DoNotVerify);
					return true;
				}

				// その他特殊パス
				if (string.Compare(paramName, "exepath", true) == 0)
				{
					replacedValue = Assembly.GetExecutingAssembly().Location;
					return true;
				}
				if (string.Compare(paramName, "exedir", true) == 0)
				{
					if (Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) is string dir) {
						replacedValue = dir;
						return true;
					}
				}

				// 置換せず
				replacedValue = paramName;
				return false;
			} // ローカル関数 ここまで


			StringBuilder sb = new();
			StringBuilder paramBuffer = new();

			bool in_percent = false;

			foreach (char c in s)
			{
				if (in_percent)
				{
					if (c == '%')
					{
						// パーセント閉じた
						if (paramBuffer.Length == 0) {
							// 開いてすぐ閉じた : エスケープされたパーセント
							sb.Append('%');
						}
						else
						{
							string resolved;
							if (!TryResolveParameter(paramBuffer.ToString(), out resolved))
							{
								// 置換されなかった : 元の文字列をそのまま出す
								resolved = "%" + paramBuffer.ToString() + "%";
							}
							sb.Append(resolved);
						}

						// バッファの掃除
						in_percent = false;
						paramBuffer.Clear();
					} else {
						paramBuffer.Append(c);
					}
				}
				else
				{
					if (c == '%') {
						// パーセント開始
						in_percent = true;
					}
					else
					{
						sb.Append(c);
					}
				}
			}

			if (in_percent)
			{
				// 終了時点でパーセントが閉じていなかった場合、バッファの中身をそのまま使用する
				sb.Append("%");
				sb.Append(paramBuffer.ToString());
			}

			return sb.ToString();
		}

		static void Main(string[] args)
		{
			// TODO: ここに引数解析処理を追加
			CommandLine cmdline = new(args);

			// 設定などの初期化
			Initialize(cmdline);

			// パラメーターの置換
			var baseDir = ReplaceParameter(settings.BaseDirectory);
			var archiveDir = ReplaceParameter(settings.ArchiveDirectory);
			var dateTimeFormat = ReplaceParameter(settings.DateTimeFormat);
			var excludePatterns = settings.ExcludePatterns.Select(ptn => ReplaceParameter(ptn)).Where(ptn => ptn != null).ToList();
			var makeJunction = settings.MakeJunction;

			// ログ出力のため、今日の日付ディレクトリとログファイルパスを取得
			var todayDir = GetArchiveDateDirectory(archiveDir, DateTime.Now);
			var logFilePath = Path.Combine(todayDir, "autostore.log");

			using (IDisposable closer = OpenLog(logFilePath))
			{
				if (cmdline.Parameters.Count == 0)
				{
					// 自動実行モード
					WriteLogLine(log, "*** Start ***");

					MoveFiles(baseDir, archiveDir);

					if (makeJunction) {
						RecreateJunction(Path.Combine(baseDir, "Today"), todayDir);

						var lastDay = DateTime.Now.AddDays(-1);
						string lastDayDir = GetArchiveDateDirectory(archiveDir, lastDay);
						while (!Directory.Exists(lastDayDir)) {
							lastDay = lastDay.AddDays(-1);
							lastDayDir = GetArchiveDateDirectory(archiveDir, lastDay);
						}
						RecreateJunction(Path.Combine(baseDir, "LastDay"), lastDayDir);
					}

					WriteLogLine(log, "*** End ***");
				}
				else
				{
					// ファイル・フォルダ指定モード
					WriteLogLine(log, "*** Start(Folder) ***");

					foreach (string target in cmdline.Parameters)
					{
						WriteLogLine(log, "- target: {0}", target);

						if (Directory.Exists(target))
						{
							MoveDirectory(target, archiveDir);
						}
						else
						{
							MoveFile(target, archiveDir);
						}
					}
				}
			}
		}

		/// <summary>
		/// baseDir内のファイルをすべて移動する
		/// </summary>
		private static void MoveFiles(string baseDir, string archiveDir)
		{
			foreach (var file in Directory.EnumerateFiles(baseDir))
			{
				if (file == null) continue;

				if (IsExcludeFile(file, out string exclusionReason))
				{
					WriteLogLine(log, $"{file} Skipped. ({exclusionReason})");
					continue;
				}

				MoveFile(file, archiveDir);
			}
		}

		/// <summary>
		/// 単一ファイルを移動する
		/// </summary>
		/// <param name="file">移動するファイルの古パス</param>
		private static void MoveFile(string file, string archiveDir)
		{
			var info = new FileInfo(file);
			var time = info.LastWriteTime;

			var dateDir = GetArchiveDateDirectory(archiveDir, time);

			var postfixNumber = 0;

			var newfile = Path.Combine(dateDir, Path.GetFileName(file));

			int trial = 0;
			while (true)
			{

				try
				{
					File.Move(file, newfile);

					break;
				}
				catch (IOException ioe)
				{
					WriteLogLine(log, "* {0} =x=> {1} [{2} : {3}]", file, newfile, ioe.GetType(), ioe.Message);
					var postfix = " (" + (++postfixNumber) + ")";
					newfile = Path.Combine(dateDir, Path.GetFileNameWithoutExtension(file) + postfix + Path.GetExtension(file));

					if (ioe is FileNotFoundException) break;
				}
				trial++;
				if (trial >= 50) break;
			}
			WriteLogLine(log, "* {0} ===> {1}", file, newfile);
		}

		/// <summary>
		/// 単一フォルダーを移動する
		/// </summary>
		/// <param name="dir">移動するフォルダーのフルパス</param>
		private static void MoveDirectory(string dir, string archiveDir)
		{
			var info = new FileInfo(dir);
			var time = info.LastWriteTime;

			var dateDir = GetArchiveDateDirectory(archiveDir, time);

			var postfixNumber = 0;

			var newfile = Path.Combine(dateDir, Path.GetFileName(dir));

			int trial = 0;
			while (true)
			{

				try
				{
					Directory.Move(dir, newfile);

					break;
				}
				catch (IOException ioe)
				{
					WriteLogLine(log, "* {0} =x=> {1} [{2} : {3}]", dir, newfile, ioe.GetType(), ioe.Message);
					var postfix = " (" + (++postfixNumber) + ")";
					newfile = Path.Combine(dateDir, Path.GetFileNameWithoutExtension(dir) + postfix);

					if (ioe is FileNotFoundException) break;
				}
				trial++;
				if (trial >= 50) break;
			}
			WriteLogLine(log, "* {1} ===> {2}", dir, newfile);
		}

		/// <summary>
		/// ジャンクションを作成する。
		/// </summary>
		/// <param name="junctionPath">作成するジャンクションのフルパス</param>
		/// <param name="targetPath">ジャンクションが指し示す要素のフルパス</param>
		private static void RecreateJunction(string junctionPath, string targetPath)
		{
			var jctCreate = false;
			if (Directory.Exists(junctionPath))
			{
				var di = new DirectoryInfo(junctionPath);
				if (di.Attributes.HasFlag(FileAttributes.ReparsePoint))
				{
					Directory.Delete(junctionPath);
					jctCreate = true;
				}
			}
			else if (File.Exists(junctionPath))
			{
				var di = new FileInfo(junctionPath);
				if (di.Attributes.HasFlag(FileAttributes.ReparsePoint))
				{
					File.Delete(junctionPath);
					jctCreate = true;
				}
			}
			else
			{
				jctCreate = true;
			}


			if (!jctCreate) return;
			var sblog = new StringBuilder();
			MkLink(targetPath, junctionPath, (o, e) => sblog.Append(e.Data));
			foreach (var line in sblog.ToString().Split("\n").Select(s => s.TrimEnd()))
			{
				WriteLogLine(log, $"MkLink : {line}");
			}
		}

		/// <summary>
		/// archiveDir内の格納先日付ディレクトリを取得する。そのディレクトリが存在しない場合は同時に作成する。
		/// </summary>
		/// <param name="time"></param>
		/// <returns></returns>
		private static string GetArchiveDateDirectory(string archiveDir, DateTime time)
		{
			var subdir = time.ToString(settings.DateTimeFormat);

			var dateDir = Path.Combine(archiveDir, subdir);
			if (!Directory.Exists(dateDir))
			{
				Directory.CreateDirectory(dateDir);
			}
			return dateDir;
		}

		/// <summary>
		/// 移動の対象としないファイルかどうか
		/// </summary>
		/// <param name="file"></param>
		/// <returns>移動の対象としない場合、その理由(非null文字列) / 移動対象である場合はnull</returns>
		private static bool IsExcludeFile(string file, out string reason)
		{
			// ショートカットは除外
			if (string.Compare(Path.GetExtension(file), ".lnk", StringComparison.CurrentCultureIgnoreCase) == 0)
			{
				reason = "Shortcut File";
				return true;
			}

			// desktop.iniは除外
			if (string.Compare(Path.GetFileName(file), "desktop.ini", StringComparison.CurrentCultureIgnoreCase) == 0)
			{
				reason = "desktop.ini";
				return true;
			}

			// このモジュールだった場合は除外
			if (string.Compare(Path.GetFullPath(file), Path.GetFullPath(Assembly.GetExecutingAssembly().Location), true) == 0)
			{
				reason = "Self";
				return true;
			}

			// 隠しファイルは除外
			if (File.GetAttributes(file).HasFlag(FileAttributes.Hidden))
			{
				reason = "Hidden file";
				return true;
			}

			// フィルターで指定された除外対象である場合は除外
			foreach (var pattern in settings.ExcludePatterns)
			{
				if (CheckExcludePatternMatch(file, pattern))
				{
					reason = $"Exclusion pattern '{pattern}'";
					return true;
				}
			}

			reason = "Not excluded.";
			return false;
		}

		/// <summary>
		/// ファイルパスが除外パターンにマッチするかを検査する
		/// </summary>
		/// <param name="file">検査対象の文字列(ファイルパス)</param>
		/// <param name="pattern">除外パターン</param>
		/// <returns>パターンにマッチする場合(除外対象である場合)true</returns>
		private static bool CheckExcludePatternMatch(string file, string pattern)
		{
			if (pattern.StartsWith("/") && pattern.EndsWith("/")) {
				// スラッシュで挟まれている場合は正規表現チェック
				var regexPattern = pattern.Trim('/');
				try
				{
					var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
					return regex.IsMatch(file);
				} 
				catch (ArgumentException) 
				{
					// パターンが不正の場合は一致せずとみなす
					return false;
				}
			}
			else
			{
				// 通常文字列の場合は通常チェック(大文字小文字区別なし)
				return file.ToLower().Contains(pattern.ToLower());
			}
		}

		/// <summary>
		/// cmd.exe 経由で mklink を実行しジャンクションを作成する
		/// </summary>
		/// <param name="from"></param>
		/// <param name="to"></param>
		/// <param name="logwriter"></param>
		private static void MkLink(string from, string to, Action<object, DataReceivedEventArgs> logwriter)
		{
			using (var proc = new Process())
			{
				proc.StartInfo.FileName = "cmd.exe";
				proc.StartInfo.Arguments = string.Format("/C mklink /J \"{0}\" \"{1}\"", to, from);
				proc.StartInfo.CreateNoWindow = false;
				proc.StartInfo.UseShellExecute = false;
				proc.StartInfo.RedirectStandardError = true;
				proc.StartInfo.RedirectStandardOutput = true;

				proc.OutputDataReceived += (o, e) => logwriter(o, e);
				proc.ErrorDataReceived += (o, e) => logwriter(o, e);

				proc.Start();
				proc.BeginOutputReadLine();
				proc.BeginErrorReadLine();
				if (!proc.WaitForExit(30 * 1000))
				{
					proc.Kill();
				}
			}
		}

		private static IDisposable OpenLog(string path) {
			var oldLog = log;
			log = File.AppendText(path);
			if (oldLog is StringWriter sw)
			{
				log.Write(sw.ToString());
			}

			oldLog?.Dispose();

			return log;
		}

		public static void WriteLogLine(TextWriter? writer, string str, bool timestamp = true) {
			Console.WriteLine(str);
			if (writer == null) return;
			if (timestamp) {
				writer.WriteLine($"{DateTime.Now}   {str}");
			}
			else {
				writer.WriteLine(str);
			}
		}
		public static void WriteLogLine(TextWriter? writer, string format, params object[] parameters) {
			Console.WriteLine(format, parameters);
			if (writer == null) return;
			writer.WriteLine($"{DateTime.Now} : {string.Format(format, parameters)}");
		}
	}
}
