using System;
using System.Data;
using System.IO;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using TShockAPI;
using TShockAPI.DB;

namespace Reports
{
	public class Database
	{
		private IDbConnection _db;

		public Database(IDbConnection db)
		{
			_db = db;

			var sqlCreator = new SqlTableCreator(_db,
				_db.GetSqlType() == SqlType.Sqlite
					? (IQueryBuilder)new SqliteQueryCreator()
					: new MysqlQueryCreator());

			var table = new SqlTable("Reports",
			   new SqlColumn("ReportID", MySqlDbType.Int32) { AutoIncrement = true, Primary = true },
			   new SqlColumn("UserID", MySqlDbType.Int32),
			   new SqlColumn("ReportedID", MySqlDbType.Int32),
			   new SqlColumn("Message", MySqlDbType.Text),
			   new SqlColumn("Position", MySqlDbType.Text),
			   new SqlColumn("Time", MySqlDbType.Int32));

			sqlCreator.EnsureExists(table);
		}

		public static Database InitDb(string name)
		{
			IDbConnection db;
			if (TShock.Config.StorageType.ToLower() == "sqlite")
				db =
					new SqliteConnection(string.Format("uri=file://{0},Version=3",
						Path.Combine(TShock.SavePath, name + ".sqlite")));
			else if (TShock.Config.StorageType.ToLower() == "mysql")
			{
				try
				{
					var host = TShock.Config.MySqlHost.Split(':');
					db = new MySqlConnection
					{
						ConnectionString = String.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4}",
							host[0],
							host.Length == 1 ? "3306" : host[1],
							TShock.Config.MySqlDbName,
							TShock.Config.MySqlUsername,
							TShock.Config.MySqlPassword
							)
					};
				}
				catch (MySqlException x)
				{
					Log.Error(x.ToString());
					throw new Exception("MySQL not setup correctly.");
				}
			}
			else
				throw new Exception("Invalid storage type.");

			var database = new Database(db);
			return database;
		}

		public QueryResult QueryReader(string query, params object[] args)
		{
			return _db.QueryReader(query, args);
		}

		public int Query(string query, params object[] args)
		{
			return _db.Query(query, args);
		}

		public bool DeleteValue(string column, object value)
		{
		    var query = string.Format("DELETE FROM Reports WHERE {0} = @0", column);
			return _db.Query(query, value) > 0;
		}
	}
}
