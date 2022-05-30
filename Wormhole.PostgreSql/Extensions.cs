using Npgsql;
using NpgsqlTypes;
namespace Wormhole.PostgreSql;

public static class Extensions
{
    public static void Set(this NpgsqlParameterCollection collection, string name, object value, NpgsqlDbType? dbType = null)
    {
        if (collection.Contains(name))
        {
            if (dbType.HasValue)
                collection[name].NpgsqlDbType = dbType.Value;
            collection[name].Value = value;
        }
        else
        {
            if (dbType.HasValue)
                collection.AddWithValue(name, dbType.Value, value);
            else
                collection.AddWithValue(name, value);
        }
    }
}
