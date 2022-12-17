using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Specialized;

namespace AutoStore;
internal class CommandLine {
	private Dictionary<string, string?> options = new();
	private List<string> parameters = new();
	private List<string> arguments = new();

	public CommandLine(string[] args) {
		this.arguments .AddRange(args);

		foreach(string arg in arguments) {
			string[]? keyvalue = null;
			if (arg.StartsWith("--")) {
				keyvalue = arg.Substring(2).Split(':');
			} else if (arg.StartsWith("/")) {
				keyvalue = arg.Substring(1).Split(':');
			}

			if (keyvalue != null && keyvalue.Length >= 1) {
				var key = keyvalue[0].ToLowerInvariant();
				var val = keyvalue.Length >= 2 ? keyvalue[1] : null;
				options.Add(key, val);
				continue;
			}

			// オプションスイッチ出ない場合はパラメーターとして格納
			parameters.Add(arg);
		}
	}

	public IReadOnlyCollection<string> Parameters { get { return parameters; } }
	public IReadOnlyDictionary<string, string?> Options { get { return options; } }

	public IReadOnlyList<string> AllArguments { get { return arguments;} }

	public bool HasOption(string key) { return options.ContainsKey(key); }
}
