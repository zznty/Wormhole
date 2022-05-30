using System.Data.Common;
using System.Runtime.CompilerServices;
using NLog;
using Npgsql;
using Npgsql.BackendMessages;
using Npgsql.Internal;
using Npgsql.Internal.TypeHandling;
using Npgsql.PostgresTypes;
using Npgsql.TypeMapping;
using PetaPoco;
using PetaPoco.Core.Inflection;
using PetaPoco.Providers;
using Torch.API;
using Torch.Managers;
using Torch.Utils;
namespace Wormhole.PostgreSql.Managers;

public class DbManager : Manager
{
    [ReflectedMethod(TypeName = "Npgsql.TypeMapping.ConnectorTypeMapper, Npgsql, Version=6.0.4", Name = "ApplyUserMapping")]
    private static readonly Action<INpgsqlTypeMapper, PostgresType, Type, NpgsqlTypeHandler> ApplyMapping = null!;
    
    [ReflectedGetter(TypeName = "Npgsql.TypeMapping.ConnectorTypeMapper, Npgsql, Version=6.0.4", Name = "DatabaseInfo")]
    private static readonly Func<INpgsqlTypeMapper, NpgsqlDatabaseInfo> DbInfoGetter = null!;

    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();
    private readonly Config _credentials;
    private string _connString = null!;
    public NpgsqlConnection Connection { get; private set; } = null!;
    public NpgsqlLargeObjectManager ObjectManager { get; private set; } = null!;
    
    public IDatabase Db { get; private set; } = null!;
    
    public DbManager(ITorchBase torchInstance, Config credentials) : base(torchInstance)
    {
        _credentials = credentials;
    }

    public NpgsqlConnection CreateConnection() => new(_connString);

    public override void Attach()
    {
        base.Attach();
        _connString = $"Host={_credentials.Host};Port={_credentials.Port};Username={_credentials.Username};Password={_credentials.Password};Database={_credentials.Database};Keepalive=45";
        Connection = new(_connString);
        Log.Info("Connecting to the database");
        Connection.Open();
        ObjectManager = new(Connection);

        MapTypes();
        
        Db = DatabaseConfiguration.Build()
            .UsingProvider<Provider>()
            .WithAutoSelect()
            .UsingDefaultMapper<ConventionMapper>(mapper =>
            {
                string UnFuckIt(IInflector inflector, string s) => inflector.Camelise(inflector.Underscore(s).ToLower());
                mapper.InflectColumnName = UnFuckIt;
                mapper.InflectTableName = UnFuckIt;
            })
            .UsingConnection(Connection)
            .Create();
    }
    
    private void MapTypes()
    {
        var info = DbInfoGetter(Connection.TypeMapper);
        var resolvers = Connection.TypeMapper.GetPrivateField<TypeHandlerResolver[]>("_resolvers");
        
        NpgsqlTypeHandler? oidHandler = null;
        foreach (var resolver in resolvers)
        {
            if (oidHandler is null && resolver.ResolveByDataTypeName("oid") is { } h)
                oidHandler = h;
        }

        ApplyMapping(Connection.TypeMapper, info.GetPostgresTypeByName("oid"), typeof(uint), oidHandler!);
    }

    public override void Detach()
    {
        base.Detach();
        Log.Info("Disconnecting from the database");
        Connection.Close();
    }

    private class Provider : PostgreSQLDatabaseProvider
    {
        // Because fucking torch assembly resolver too stupid to be able to compare assembly names without versions 
        public override DbProviderFactory GetFactory() => NpgsqlFactory.Instance;
    }
}
