using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace AutoStore;
internal class Settings : ICloneable {
	// 片づけるフォルダ
	public string BaseDirectory { get; protected set; }
	// 格納先フォルダ
	public string ArchiveDirectory { get; protected set; }
	// 日時ディレクトリ書式(DateTime.ToStringに渡される)
	public string DateTimeFormat { get; protected set; }

	List<string> excludePatterns = new List<string>();

	public IReadOnlyList<string> ExcludePatterns => excludePatterns;

	public bool MakeJunction { get; protected set; }

	private Settings(string baseDirectory, string archiveDirectory, string dateTimeFormat, List<string> excludePatterns, bool makeJunction) {
		BaseDirectory = baseDirectory;
		ArchiveDirectory = archiveDirectory;
		DateTimeFormat = dateTimeFormat;
		this.excludePatterns = excludePatterns;
		MakeJunction = makeJunction;
	}

	private Settings(string baseDirectory, string archiveSubFolderName, string dateTimeFormat)
		: this(baseDirectory, Path.Combine(baseDirectory, archiveSubFolderName), dateTimeFormat, new List<string>(), true) {
	}

	public Settings() : this(
		Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
		"Files",
		@"yyyyMM\\yyyyMMdd"
	) { }

	object ICloneable.Clone() {
		return new Settings(BaseDirectory, ArchiveDirectory, DateTimeFormat, excludePatterns, MakeJunction);
	}

	public bool ReadIni(string filePath, bool appendList) {
		try {
			using (var reader = File.OpenText(filePath)) {
				ReadIniInternal(reader, appendList);
			}
			return true;
		}
		catch { return false; }
	}

	public bool ReadIni(TextReader textReader, bool appendList) {
		try {
			ReadIniInternal(textReader, appendList);
			return true;
		} catch { return false; }
	}

	private void ReadIniInternal(TextReader reader, bool appendList) {
		var excludeList = appendList ? excludePatterns : new List<string>();

		while (true) {
			var line = reader.ReadLine();
			if (line == null) break;
			if (line.Length == 0) continue; // 空の行
			if (line[0] == '#') continue; // コメント

			var kv = line.Split(new[] { '=' }, 2).Select(s => s.Trim()).ToArray();
			if (kv.Length == 2) {
				switch (kv[0].ToLower()) {
					case "basedir":
					case "base_dir":
						BaseDirectory = kv[1];
						break;
					case "archivedir":
					case "archive_dir":
						ArchiveDirectory = kv[1];
						break;
					case "datetimeformat":
					case "datetime_format":
						DateTimeFormat = kv[1];
						break;
					case "exclude":
						excludeList.Add(kv[1]);
						break;
					case "makejunction":
					case "make_junction":
						if (int.TryParse(kv[1], out int val)) {
							MakeJunction = (val != 0);
						}
						else {
							MakeJunction = kv[1].ToLower() switch {
								"off" => false,
								"no" => false,
								"" => false,
								_ => true
							};
						}
						break;
				}
			}
		}

		excludePatterns = excludeList;
		
	}
}
