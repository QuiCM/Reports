using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace Reports
{
	public class Config
	{
		/// <summary>
		/// Header displayed on reports with the <see cref="ReportState"/> flag set to <see cref="ReportState.Unread"/>
		/// </summary>
		public string UnreadReportHeader = "[Unread]";
		/// <summary>
		/// Header displayed on reports with the <see cref="ReportState"/> flag set to <see cref="ReportState.Unhandled"/>
		/// </summary>
		public string UnhandledReportHeader = "[Unhandled]";
		/// <summary>
		/// Header displayed on reports with the <see cref="ReportState"/> flag set to <see cref="ReportState.Handled"/>
		/// </summary>
		public string DefaultReportHeader = "";
		/// <summary>
		/// Key/Value pair collection of phrases that trigger the display of warnings
		/// </summary>
		public Dictionary<string, string> PhraseWarnings = new Dictionary<string, string>();

		/// <summary>
		/// Writes all the data contained in this class to the given file path
		/// </summary>
		/// <param name="path"></param>
		public void Write(string path)
		{
			using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write))
			{
				Write(fs);
			}
		}

		/// <summary>
		/// Writes all the data contained in this class to the given file path
		/// </summary>
		/// <param name="stream"></param>
		public void Write(Stream stream)
		{
			string str = JsonConvert.SerializeObject(this, Formatting.Indented);
			using (StreamWriter sw = new StreamWriter(stream))
			{
				sw.Write(str);
			}
		}

		/// <summary>
		/// Reads data from the given file path into this class
		/// </summary>
		/// <param name="path"></param>
		public void Read(string path)
		{
			if (!File.Exists(path))
				return;
			using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				Read(fs);
			}
		}

		/// <summary>
		/// Reads data from the given file path into this class
		/// </summary>
		/// <param name="stream"></param>
		public void Read(Stream stream)
		{
			using (StreamReader sr = new StreamReader(stream))
			{
				Config c = JsonConvert.DeserializeObject<Config>(sr.ReadToEnd());
				PhraseWarnings = c.PhraseWarnings;
				UnreadReportHeader = c.UnreadReportHeader;
				UnhandledReportHeader = c.UnhandledReportHeader;
				DefaultReportHeader = c.DefaultReportHeader;
			}
		}
	}
}
