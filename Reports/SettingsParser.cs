using System;
using System.IO;

namespace Reports
{
	public class SettingsParser
	{
		private const string Comment = "#";
		private const char Separator = '=';
		private static string[] _fileStrings;
		private static string _filepath;
		private static bool _fileLoaded;

		public static void CreateFile(string filepath, string[] text)
		{
			File.WriteAllLines(filepath, text);
			_fileStrings = text;
			_filepath = filepath;
			_fileLoaded = true;
		}

		public static void LoadFromFile(string filepath)
		{
			if (!File.Exists(filepath))
			{
				throw new FileNotFoundException("Failed to find file", filepath);
			}

			_filepath = filepath;
			_fileStrings = File.ReadAllLines(filepath);
			_fileLoaded = true;
		}

		/// <summary>
		/// Gets the value of the specified key in the file
		/// </summary>
		/// <typeparam name="T">Data type of the value</typeparam>
		/// <param name="key">key to retrieve from</param>
		/// <param name="value">object to store return value</param>
		public static void Get<T>(string key, ref T value)
		{
			if (!_fileLoaded)
			{
				throw new InvalidOperationException("No file has been loaded");
			}

			for (var i = 0; i < _fileStrings.Length; i++)
			{
				if (_fileStrings[i].StartsWith(Comment))
				{
					continue;
				}

				var pair = _fileStrings[i].Split(Separator);
				if (string.Equals(pair[0], key, StringComparison.InvariantCultureIgnoreCase))
				{
					try
					{
						value = (T)Convert.ChangeType(pair[1], typeof(T));
					}
					catch (Exception)
					{
						throw new Exception(string.Format("Type '{0}' does not match value '{1}' on line {2}",
							typeof(T).Name, pair[1], i + 1));
					}
					break;
				}
			}
		}

		/// <summary>
		/// Sets the value of the specified key
		/// </summary>
		/// <typeparam name="T">Data type of the value</typeparam>
		/// <param name="key">key to modify</param>
		/// <param name="value">new value</param>
		public static void Set<T>(string key, T value)
		{
			if (!_fileLoaded)
			{
				throw new InvalidOperationException("No file has been loaded");
			}

			for (var i = 0; i < _fileStrings.Length; i++)
			{
				if (_fileStrings[i].StartsWith(Comment))
				{
					continue;
				}

				var pair = _fileStrings[i].Split(Separator);
				if (string.Equals(pair[0], key, StringComparison.InvariantCultureIgnoreCase))
				{
					_fileStrings[i] = string.Format("{0}={1}", key, value);
					break;
				}
			}

			File.WriteAllLines(_filepath, _fileStrings);
		}
	}
}
