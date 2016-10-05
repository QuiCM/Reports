using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace Reports
{
	[ApiVersion(1, 25)]
	public class Reports : TerrariaPlugin
	{
		private Config config = new Config();
		private static Database Db { get; set; }

		internal static string UnreadReportHeader;
		internal static string UnhandledReportHeader;
		internal static string DefaultReportHeader;

		private readonly Vector2[] _teleports = new Vector2[Main.player.Length];
		private readonly Report[] _report = new Report[Main.player.Length];

		public override string Author
		{
			get { return "White"; }
		}

		public override string Description
		{
			get { return "Allows players to report players"; }
		}

		public override string Name
		{
			get { return "Reports"; }
		}

		public override Version Version
		{
			get { return new Version(1, 2); }
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
				ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
				ServerApi.Hooks.ServerChat.Deregister(this, OnServerChat);
			}
			base.Dispose(disposing);
		}

		public override void Initialize()
		{
			PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
			ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
			ServerApi.Hooks.ServerChat.Register(this, OnServerChat);

			string path = Path.Combine(TShock.SavePath, "Reports.json");
			if (!File.Exists(path))
			{
				config.Write(path);
			}
			config.Read(path);

			UnreadReportHeader = config.UnreadReportHeader;
			UnhandledReportHeader = config.UnhandledReportHeader;
			DefaultReportHeader = config.DefaultReportHeader;

			Db = Database.InitDb("Reports");

			Commands.ChatCommands.Add(new Command("reports.report", Report, "report")
			{
				AllowServer = false,
				HelpDesc = new[]
				{
					"Create an admin-viewable report for a player",
					"Usage: /report <player> [reason]"
				}
			});
			Commands.ChatCommands.Add(new Command("reports.report.check", CheckReports, "creport", "creports",
				"checkreports")
			{
				HelpDesc = new[]
				{
					"View any reports filed by players",
					"Usage: /creports [search|id|page <number>]"
				}
			});
			Commands.ChatCommands.Add(new Command("reports.report.teleport", RTeleport, "rtp", "rteleport")
			{
				HelpDesc = new[]
				{
					"Teleports you to the location your last read report was created at",
					"Usage: /rtp"
				},
				AllowServer = false
			});
			Commands.ChatCommands.Add(new Command("reports.report.delete", DeleteReports, "dreport", "dreports",
				"deletereports")
			{
				HelpDesc = new[]
				{
					"Deletes a report, or a range of reports",
					"Usage: /dreports id",
					"Usage: /dreports id id2 id3 ... idn"
				}
			});
			Commands.ChatCommands.Add(new Command("reports.report.handle", HandleReports, "hreport", "hreports", "handle")
			{
				HelpDesc = new[]
				{
					"Set a handled state on a single report, or range of reports.",
					"This means that they will not be displayed as new reports."
				}
			});
			Commands.ChatCommands.Add(new Command("reports.reload", ReloadSettings, "rsettings")
			{
				HelpDesc = new[]
				{
					"Reloads the ReportSettings.txt file"
				}
			});
			Commands.ChatCommands.Add(new Command("reports.admin.reseed", ReseedDb, "reseed")
			{
				HelpDesc = new[]
				{
					"Reseeds the auto-increment value of the reports database.",
					"You should only use this if you know what the above sentence means.",
					"Your reports database must be empty for this command to work."
				}
			});
		}

		private void OnServerChat(ServerChatEventArgs args)
		{
			//Get the player who sent the message
			TSPlayer ply = TShock.Players[args.Who];
			if (ply == null)
			{
				//if the player is null, return straight away.
				return;
			}

			//Check each phrase to see if it's contained in the text
			foreach (KeyValuePair<string, string> pair in config.PhraseWarnings)
			{
				if (args.Text.Contains(pair.Key))
				{
					//if it is, send the assosciated message
					ply.SendMessage($"<From ChatWatch> {pair.Value}", Color.MediumPurple);
					//then break, so we don't send multiple messages if multiple phrases are contained
					break;
				}
			}
		}

		private void OnGreetPlayer(GreetPlayerEventArgs args)
		{
			if (TShock.Players[args.Who].IsLoggedIn)
			{
				OnPlayerPostLogin(new PlayerPostLoginEventArgs(TShock.Players[args.Who]));
			}
		}

		private void OnPlayerPostLogin(PlayerPostLoginEventArgs args)
		{
			if (!args.Player.Group.HasPermission("reports.report.check"))
			{
				return;
			}

			//Pull unhandled reports from the db
			var newCount = 0;
			var unhandledCount = 0;
			using (var reader = Db.QueryReader("SELECT * FROM Reports"))
			{
				while (reader.Read())
				{
					bool unread = reader.Get<int>("State") == 0;
					bool unhandled = reader.Get<int>("State") == 1;

					if (unread)
					{
						newCount++;
					}
					if (unhandled)
					{
						unhandledCount++;
					}
				}
			}
			if (newCount > 0)
			{
				args.Player.SendWarningMessage("There are {0} unread, and {1} unhandled report{2} to view. Use /checkreports",
					newCount, unhandledCount, Suffix(unhandledCount));
			}
		}

		private void ReseedDb(CommandArgs args)
		{
			string query = Db.MySQL ? "ALTER TABLE Reports auto_increment = 0;"
				: "UPDATE SQLITE_SEQUENCE SET seq = 0 WHERE name = 'Reports'";

			using (var reader = Db.QueryReader("SELECT ReportID FROM Reports"))
			{
				if (reader.Read())
				{
					args.Player.SendErrorMessage("Your reports database is not empty! Cannot reseed.");
					return;
				}
			}

			bool success = Db.Query(query) > 0;

			if (success)
			{
				args.Player.SendSuccessMessage("Successfully reseeded the reports database.");
			}
			else
			{
				args.Player.SendErrorMessage("Reseed failed.");
			}
		}

		private void ReloadSettings(CommandArgs args)
		{
			string path = Path.Combine(TShock.SavePath, "Reports.json");
			if (!File.Exists(path))
			{
				config.Write(path);
			}
			config.Read(path);

			UnreadReportHeader = config.UnreadReportHeader;
			UnhandledReportHeader = config.UnhandledReportHeader;
			DefaultReportHeader = config.DefaultReportHeader;

			args.Player.SendSuccessMessage("Reloaded Reports.json");
		}

		private void HandleReports(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendErrorMessage("Invalid usage. /hreports [id] <id2 id3 id4 ... idn>");
				return;
			}
			var failures = new List<int>();
			var nonparsed = 0;
			foreach (var str in args.Parameters)
			{
				int id;
				if (!int.TryParse(str, out id))
				{
					args.Player.SendErrorMessage(str + " is not a valid report ID and has been skipped");
					nonparsed++;
					continue;
				}
				if (!Db.SetValue("State", 2, "ReportID", id))
				{
					failures.Add(id);
				}
			}
			if (failures.Count > 0)
			{
				args.Player.SendErrorMessage("The following reports failed to be updated: {0}",
					string.Join(", ", failures));
			}
			else
			{
				args.Player.SendSuccessMessage("Updated {0} report{1}.", args.Parameters.Count - nonparsed,
					Suffix(args.Parameters.Count - nonparsed));
			}
		}

		private void DeleteReports(CommandArgs args)
		{
			if (args.Parameters.Count < 1)
			{
				args.Player.SendErrorMessage("Invalid usage. /dreports [id] <id2 id3 id4 ... idn>");
				return;
			}
			var failures = new List<int>();
			var nonparsed = 0;
			foreach (var str in args.Parameters)
			{
				int id;
				if (!int.TryParse(str, out id))
				{
					args.Player.SendErrorMessage(str + " is not a valid report ID and has been skipped");
					nonparsed++;
					continue;
				}
				if (!Db.DeleteValue("ReportID", id))
				{
					failures.Add(id);
				}
			}
			if (failures.Count > 0)
			{
				args.Player.SendErrorMessage("The following reports failed to be deleted: {0}",
					string.Join(", ", failures));
			}
			else
			{
				args.Player.SendSuccessMessage("Deleted {0} report{1}.", args.Parameters.Count - nonparsed,
					Suffix(args.Parameters.Count - nonparsed));
			}
		}

		private void RTeleport(CommandArgs args)
		{
			if (_teleports[args.Player.Index] == new Vector2())
			{
				args.Player.SendErrorMessage("You have no report location to move to.");
				return;
			}
			args.Player.Teleport(_teleports[args.Player.Index].X, _teleports[args.Player.Index].Y);
			args.Player.SendSuccessMessage("You have been moved to the location of report #{0}",
				_report[args.Player.Index].ReportID);
		}

		private void CheckReports(CommandArgs args)
		{
			//Pull unhandled reports from the db
			var reports = new List<Report>();
			using (var reader = Db.QueryReader("SELECT * FROM Reports"))
			{
				while (reader.Read())
				{
					reports.Add(new Report(
						reader.Get<int>("ReportId"),
						reader.Get<int>("UserID"),
						reader.Get<int>("ReportedID"),
						reader.Get<string>("Message"),
						reader.Get<string>("Position"),
						reader.Get<int>("State"))
						);
				}
			}
			if (reports.Count < 1)
			{
				args.Player.SendSuccessMessage("There are no reports to view.");
				return;
			}

			IOrderedEnumerable<Report> orderedReports =
				from r in reports orderby r.ReportID descending orderby r.State ascending select r;

			if (args.Parameters.Count == 0)
			{
				PaginationTools.SendPage(args.Player, 1, orderedReports.Select(r => r.ToString()).ToList(),
					new PaginationTools.Settings
					{
						HeaderFormat = "Report IDs. Use /checkreports <id> to check a specific report. Page {0} of {1}",
						FooterFormat = "Use /checkreports page {0} for more"
					});
			}

			else if (args.Parameters.Count >= 1)
			{
				if (String.Equals(args.Parameters[0], "page", StringComparison.InvariantCultureIgnoreCase))
				{
					int pageNumber;
					if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
					{
						return;
					}

					PaginationTools.SendPage(args.Player, pageNumber, orderedReports.Select(r => r.ToString()).ToList(),
						new PaginationTools.Settings
						{
							HeaderFormat =
								"Report IDs. Use /checkreports <id> to check a specific report. Page {0} of {1}",
							FooterFormat = "Use /checkreports page {0} for more"
						});
					return;
				}

				Report report;
				int searchId;
				if (!int.TryParse(args.Parameters[0], out searchId))
				{
					var search = string.Join(" ", args.Parameters.Skip(1));
					var matches = reports.Where(r => r.Message.ToLower().Contains(search.ToLower())).ToList();
					if (matches.Count < 1)
					{
						args.Player.SendErrorMessage("No report messages matched your search '{0}'", search);
						return;
					}
					if (matches.Count > 1)
					{
						SendMultipleMatches(args.Player, matches.Select(m => m.ToString()));
						return;
					}
					report = matches[0];
				}
				else
				{
					report = reports.FirstOrDefault(r => r.ReportID == searchId);
					if (report == null)
					{
						args.Player.SendErrorMessage("No report ID matched your search '{0}'", searchId);
						return;
					}
				}
				
				if (report.Header != string.Empty)
				{
					args.Player.SendWarningMessage(string.Format("----{0}-----", report.Header));
				}
				args.Player.SendSuccessMessage("Report ID: #{0}", report.ReportID);
				if (report.ReportedID != -1)
				{
					args.Player.SendSuccessMessage("Reported user: {0}", TShock.Users.GetUserByID(report.ReportedID).Name);
				}
				args.Player.SendSuccessMessage("Reported by: {0} at position ({1})",
					TShock.Users.GetUserByID(report.UserID).Name, report.x + "," + report.y);
				args.Player.SendSuccessMessage("Report reason: {0}", report.Message);
				if (args.Player.Index > _teleports.Length || args.Player.Index < 0)
				{
					args.Player.SendWarningMessage("Failed to assign a teleport for this report.");
					args.Player.SendWarningMessage("Please make sure you are logged in while using this command");
					return;
				}
				_teleports[args.Player.Index] = new Vector2(report.x, report.y);
				_report[args.Player.Index] = report;
				args.Player.SendWarningMessage("Use /rteleport to move to the report location.");

				Db.SetValue("State", 1, "ReportID", report.ReportID);
			}
		}

		private void Report(CommandArgs args)
		{
			if (!args.Player.IsLoggedIn)
			{
				args.Player.SendErrorMessage("You must be logged in to use this command.");
				return;
			}

			//report player reason
			if (args.Parameters.Count < 1)
			{
				args.Player.SendErrorMessage("Invalid report. Usage: /report <player/reason> [reason]");
				return;
			}

			var users = TShock.Users.GetUsersByName(args.Parameters[0]);
			User user;
			string message;
			if (users.Count == 0)
			{
				message = string.Join(" ", args.Parameters);
				AddReport(args.Player, message);
				return;
			}
			if (users.Count > 1)
			{
				user = users.FirstOrDefault(u => u.Name.ToLowerInvariant().StartsWith(args.Parameters[0].ToLowerInvariant()));

				if (user == null)
				{
					TShock.Utils.SendMultipleMatchError(args.Player, users.Select(u => u.Name));
					return;
				}
			}
			else
			{
				user = users[0];
			}

			message = args.Parameters.Count > 1 ? string.Join(" ", args.Parameters.Skip(1)) : "No reason defined";
			AddReport(user, args.Player, message);			
		}

		private void AddReport(TSPlayer reportee, string message)
		{
			var success =
				Db.Query("INSERT INTO Reports (UserID, ReportedID, Message, Position, State) VALUES "
						 + "(@0, -1, @1, @2, @3)",
					reportee.User.ID, message,
					reportee.TPlayer.position.X + ":" + reportee.TPlayer.position.Y, 0) > 0;

			int id = 0;

			using (var reader = Db.QueryReader("SELECT MAX(ReportID) AS ReportID FROM Reports"))
			{
				if (reader.Read())
				{
					id = reader.Get<int>("ReportID");
				}
			}

			if (success)
			{
				reportee.SendSuccessMessage("Successfully created report.");
				reportee.SendSuccessMessage("Reason: {0}", message);
				reportee.SendSuccessMessage("Position: ({0},{1})", (int)reportee.TPlayer.position.X, (int)reportee.TPlayer.position.Y);
				TShock.Players.Where(p => p != null && p.ConnectionAlive && p.RealPlayer)
					.ForEach(p =>
					{
						if (p.Group.HasPermission("reports.report.check"))
						{
							p.SendWarningMessage("{0} has filed a report. Use /creports {1} to view it.",
								reportee.Name, id);
						}
					});
			}
			else
			{
				reportee.SendErrorMessage("Report was not successful. Please check logs for details");
			}
		}

		private void AddReport(User reported, TSPlayer reportee, string message)
		{
			var success =
				Db.Query("INSERT INTO Reports (UserID, ReportedID, Message, Position, State) VALUES "
						 + " (@0, @1, @2, @3, @4)",
					reportee.User.ID, reported.ID, message,
					reportee.TPlayer.position.X + ":" + reportee.TPlayer.position.Y, 0) > 0;
			
			int id = 0;

			using (var reader = Db.QueryReader("SELECT MAX(ReportID) AS ReportID FROM Reports"))
			{
				if (reader.Read())
				{
					id = reader.Get<int>("ReportID");
				}
			}

			if (success)
			{
				reportee.SendSuccessMessage("Successfully reported {0}.", reported.Name);
				reportee.SendSuccessMessage("Reason: {0}", message);
				reportee.SendSuccessMessage("Position: ({0},{1})", (int)reportee.TPlayer.position.X, (int)reportee.TPlayer.position.Y);
				TShock.Players.Where(p => p != null && p.ConnectionAlive && p.RealPlayer)
					.ForEach(p =>
					{
						if (p.Group.HasPermission("reports.report.check"))
						{
							p.SendWarningMessage("{0} has reported {1}. Use /creports {2} to view it.",
								reportee.Name, reported.Name, id);
						}
					});
			}
			else
			{
				reportee.SendErrorMessage("Report was not successful. Please check logs for details");
			}
		}

		public Reports(Main game)
			: base(game)
		{
		}

		private void SendMultipleMatches(TSPlayer player, IEnumerable<string> matches)
		{
			player.SendErrorMessage("Multiple reports IDs found matching your query: {0}", string.Join(", ", matches));
			player.SendErrorMessage("Use \"my query\" for items with spaces");
		}

		private string Suffix(int number)
		{
			return number == 0 || number > 1 ? "s" : "";
		}
	}

	internal class Report
	{
		public int ReportID;
		public int UserID;
		public int ReportedID;
		public string Message;
		public float x;
		public float y;
		public ReportState State;

		/// <summary>
		/// Gets the header value of this report based on its <see cref="ReportState"/>
		/// </summary>
		public string Header { get { return GetHeader(); } }

		public Report(int id, int userid, int reporterid, string message, string xy, int state)
		{
			ReportID = id;
			UserID = userid;
			ReportedID = reporterid;
			Message = message;
			x = float.Parse(xy.Split(':')[0]);
			y = float.Parse(xy.Split(':')[1]);
			State = (ReportState)state;
		}

		/// <summary>
		/// Gets the report header for this report based on its current <see cref="ReportState"/>
		/// </summary>
		/// <param name="cfg"></param>
		/// <returns></returns>
		private string GetHeader()
		{
			switch (State)
			{
				case ReportState.Unread:
					return Reports.UnreadReportHeader;
				case ReportState.Unhandled:
					return Reports.UnhandledReportHeader;
				case ReportState.Handled:
					return Reports.DefaultReportHeader;
			}

			return string.Empty;
		}

		public override string ToString()
		{
			return $"{Header} {ReportID}";
		}
	}

	internal enum ReportState
	{
		Unread = 0,
		Unhandled = 1,
		Handled = 2
	}
}