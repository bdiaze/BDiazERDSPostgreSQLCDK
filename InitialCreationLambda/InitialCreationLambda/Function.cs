using Amazon.Lambda.Core;
using MateoAPI.Helpers;
using Newtonsoft.Json;
using Npgsql;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace InitialCreationLambda;

public class Function {
    
    /// <summary>
    /// A simple function that takes a string and does a ToUpper
    /// </summary>
    /// <param name="input"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public string FunctionHandler(string input, ILambdaContext context) {
        string secretArnConnectionString = Environment.GetEnvironmentVariable("SECRET_ARN_CONNECTION_STRING") ?? throw new ArgumentNullException("SECRET_ARN_CONNECTION_STRING");
        string subapp01Name = Environment.GetEnvironmentVariable("SUBAPP_01_NAME") ?? throw new ArgumentNullException("SUBAPP_01_NAME");
        string subapp02Name = Environment.GetEnvironmentVariable("SUBAPP_02_NAME") ?? throw new ArgumentNullException("SUBAPP_02_NAME");

        Dictionary<string, string> connectionString = SecretManager.ObtenerSecreto(secretArnConnectionString).Result;

        List<string> retorno = [];

        using (NpgsqlConnection conn = new(
            $"Server={connectionString["Host"]};Port={connectionString["Port"]};SslMode=prefer;" +
            $"Database={connectionString["DefaultDatabase"]};User Id={connectionString["MasterUser"]}; Password='{connectionString["MasterPassword"]}';")) {

            conn.Open();

            // Se crea database subapp02...
            string subapp02Database = connectionString[$"{subapp02Name}Database"];
            try {
                using NpgsqlCommand cmd = new($"CREATE DATABASE \"{subapp02Database}\"", conn);
                cmd.ExecuteNonQuery();
            } catch (Exception ex) {
                string mensaje = "Error al crear base de datos de la subapp02: " + ex;
                LambdaLogger.Log(mensaje);
                retorno.Add(mensaje);
            }

            // Se crea usuario administrador subapp02...
            string subapp02AdmUsername = connectionString[$"{subapp02Name}AdmUsername"];
            string subapp02AdmPassword = connectionString[$"{subapp02Name}AdmPassword"];
            try {
                using NpgsqlCommand cmd = new($"CREATE USER \"{subapp02AdmUsername}\" WITH ENCRYPTED PASSWORD '{subapp02AdmPassword}' CREATEROLE", conn);
                cmd.ExecuteNonQuery();
            } catch (Exception ex) {
                string mensaje = "Error al crear usuario administrador de la subapp02: " + ex;
                LambdaLogger.Log(mensaje);
                retorno.Add(mensaje);
            }

            // Se otorgan permisos al nuevo usuario administrador subapp02 sobre la base creada...
            try {
                using NpgsqlCommand cmd = new($"GRANT ALL PRIVILEGES ON DATABASE \"{subapp02Database}\" TO \"{subapp02AdmUsername}\" WITH GRANT OPTION", conn);
                cmd.ExecuteNonQuery();
            } catch (Exception ex) {
                string mensaje = "Error al otorgar permisos al usuario administrador de la subapp02: " + ex;
                LambdaLogger.Log(mensaje);
                retorno.Add(mensaje);
            }
        }

        return JsonConvert.SerializeObject(retorno);
    }
}
