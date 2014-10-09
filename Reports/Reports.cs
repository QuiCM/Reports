using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using MySql.Data.MySqlClient;
using Terraria;
using TerrariaApi.Server;

using TSDB;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace Reports
{
    [ApiVersion(1, 16)]
    public class Reports : TerrariaPlugin
    {
        private static Database Db { get; set; }

        private readonly Vector2[] _teleports = new Vector2[255];
        private Report[] _report = new Report[255];

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
            get { return new Version(1, 0); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                PlayerHooks.PlayerPostLogin -= OnPlayerPostLogin;
                ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
            }
            base.Dispose(disposing);
        }

        public override async void Initialize()
        {
            PlayerHooks.PlayerPostLogin += OnPlayerPostLogin;
            ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);

            var table = new DbSqlTable("Reports",
                new DbSqlColumn("ReportID", MySqlDbType.Int32) {AutoIncrement = true, Primary = true},
                new DbSqlColumn("UserID", MySqlDbType.Int32),
                new DbSqlColumn("ReportedID", MySqlDbType.Int32),
                new DbSqlColumn("Message", MySqlDbType.VarChar),
                new DbSqlColumn("Position", MySqlDbType.VarChar),
                new DbSqlColumn("Time", MySqlDbType.Int32));

            Db = await TsDb.CreateDatabase("tshock/Reports.sqlite", new DbInfo(), table);

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
        }

        private void OnGreetPlayer(GreetPlayerEventArgs args)
        {
            if (TShock.Players[args.Who].IsLoggedIn)
                OnPlayerPostLogin(new PlayerPostLoginEventArgs(TShock.Players[args.Who]));
        }

        private void OnPlayerPostLogin(PlayerPostLoginEventArgs args)
        {
            if (!args.Player.Group.HasPermission("reports.report.check"))
                return;

            var reports = new List<Report>();
            using (var reader = Db.db.QueryReader("SELECT * FROM Reports WHERE Time > 0"))
            {
                while (reader.Read())
                {
                    reports.Add(new Report(
                        reader.Get<int>("ReportId"),
                        reader.Get<int>("UserID"),
                        reader.Get<int>("ReportedID"),
                        reader.Get<string>("Message"),
                        reader.Get<string>("Position"),
                        reader.Get<int>("Time")));
                }
            }
            if (reports.Count > 0)
            {
                args.Player.SendWarningMessage("There are {0} new report{1} to view. Use /checkreports",
                    reports.Count, Suffix(reports.Count));
            }
        }

        private async void DeleteReports(CommandArgs args)
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
                if (!await Db.ClearData("Reports", new SqlValue("ReportID", id)))
                    failures.Add(id);
            }
            if (failures.Count > 0)
                args.Player.SendErrorMessage("The following reports failed to be deleted: {0}",
                    string.Join(", ", failures));
            else
                args.Player.SendSuccessMessage("Deleted {0} report{1}.", args.Parameters.Count - nonparsed,
                    Suffix(args.Parameters.Count - nonparsed));
        }

        private void RTeleport(CommandArgs args)
        {
            if (_teleports[args.Player.Index] == new Vector2())
            {
                args.Player.SendErrorMessage("You have no report location to move to.");
                return;
            }
            args.Player.Teleport(_teleports[args.Player.Index].X, _teleports[args.Player.Index].Y);
            args.Player.SendSuccessMessage("You have been moved to the location that report ID {0} was created at",
                _report[args.Player.Index].ReportID);
        }

        private void CheckReports(CommandArgs args)
        {
            var reports = new List<Report>();
            using (var reader = Db.db.QueryReader("SELECT * FROM Reports WHERE Time > 0"))
            {
                while (reader.Read())
                {
                    reports.Add(new Report(
                        reader.Get<int>("ReportId"),
                        reader.Get<int>("UserID"),
                        reader.Get<int>("ReportedID"),
                        reader.Get<string>("Message"),
                        reader.Get<string>("Position"),
                        reader.Get<int>("Time")));
                }
            }
            if (reports.Count < 1)
            {
                args.Player.SendSuccessMessage("There are no reports to view.");
                return;
            }

            if (args.Parameters.Count == 0)
            {
                PaginationTools.SendPage(args.Player, 1, reports.Select(r => r.ReportID).ToList(),
                    new PaginationTools.Settings
                    {
                        HeaderFormat = "Report IDs. Use /checkreports <id> to check a specific report. Page {0} of {1}",
                        FooterFormat = "Use /checkreports page {0} for more"
                    });
                return;
            }

            if (args.Parameters.Count > 1 &&
                String.Equals(args.Parameters[0], "page", StringComparison.InvariantCultureIgnoreCase))
            {
                int pageNumber;
                if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                    return;

                PaginationTools.SendPage(args.Player, pageNumber, reports.Select(r => r.ReportID).ToList(),
                    new PaginationTools.Settings
                    {
                        HeaderFormat = "Report IDs. Use /checkreports <id> to check a specific report. Page {0} of {1}",
                        FooterFormat = "Use /checkreports page {0} for more"
                    });
                return;
            }

            int searchId;
            Report report;
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
                    SendMultipleMatches(args.Player, matches.Select(m => m.ReportID));
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

            args.Player.SendSuccessMessage("Report ID: {0}", report.ReportID);
            args.Player.SendSuccessMessage("Reported user: {0}", TShock.Users.GetUserByID(report.ReportedID).Name);
            args.Player.SendSuccessMessage("Reported by: {0} at position ({1})",
                TShock.Users.GetUserByID(report.UserID).Name,
                report.x + "," + report.y);
            args.Player.SendSuccessMessage("Report reason: {0}", report.Message);
            _teleports[args.Player.Index] = new Vector2(report.x, report.y);
            _report[args.Player.Index] = report;
            args.Player.SendWarningMessage("Use /rteleport to move to the location the report was made.");
        }

        private async void Report(CommandArgs args)
        {
            //report player reason
            if (args.Parameters.Count < 1)
            {
                args.Player.SendErrorMessage("Invalid report. Usage: /report <player> [reason]");
                return;
            }
            var user = TShock.Users.GetUsers()
                .FirstOrDefault(u => u.Name.ToLower().StartsWith(args.Parameters[0].ToLower()));
            if (user == null)
            {
                var users =
                    TShock.Users.GetUsers()
                        .Where(u => u.Name.ToLower().StartsWith(args.Parameters[0].ToLower()))
                        .ToList();
                if (users.Count < 1)
                {
                    args.Player.SendErrorMessage("No player matches found for '{0}'", args.Parameters[0]);
                    return;
                }
                if (users.Count > 1)
                {
                    TShock.Utils.SendMultipleMatchError(args.Player, users.Select(u => u.Name));
                    return;
                }
                user = users[0];
            }

            var message = args.Parameters.Count > 1 ? string.Join(" ", args.Parameters.Skip(1)) : "No reason defined";

            var success =
                await Db.InsertValues("Reports", SqliteClause.IGNORE,
                    new SqlValue("UserID", args.Player.UserID),
                    new SqlValue("ReportedID", user.ID),
                    new SqlValue("Message", message),
                    new SqlValue("Position", args.TPlayer.position.X + ":" + args.TPlayer.position.Y),
                    new SqlValue("Time", 120));

            var id = await Db.RetrieveValues("Reports", "ReportID",
                new SqlValue("UserID", args.Player.UserID),
                new SqlValue("Message", message));

            if (success)
            {
                args.Player.SendSuccessMessage("Successfully filed a report for player {0}.", user.Name);
                args.Player.SendSuccessMessage("Reason: {0}", message);
                args.Player.SendSuccessMessage("Position: ({0},{1})", args.TPlayer.position.X, args.TPlayer.position.Y);
                TShock.Players.Where(p => p.Group.HasPermission("reports.report.check"))
                    .ForEach(p => p.SendWarningMessage("{0} has filed a new report. Use /creports {1} to view it.",
                        args.Player.Name, id[0]));
            }
            else
                args.Player.SendErrorMessage("Report was not successful. Please check logs for details");
        }

        public Reports(Main game)
            : base(game)
        {
        }

        private void SendMultipleMatches(TSPlayer player, IEnumerable<int> matches)
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
        public int Time;

        public Report(int id, int userid, int reporterid, string message, string xy, int time)
        {
            ReportID = id;
            UserID = userid;
            ReportedID = reporterid;
            Message = message;
            x = float.Parse(xy.Split(':')[0]);
            y = float.Parse(xy.Split(':')[1]);
            Time = time;
        }
    }
}