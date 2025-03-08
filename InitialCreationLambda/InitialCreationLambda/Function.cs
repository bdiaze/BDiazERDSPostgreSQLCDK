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

        dynamic connectionString = SecretManager.ObtenerSecreto(secretArnConnectionString).Result;

        List<string> retorno = [];

        using (NpgsqlConnection conn = new(
            $"Server={connectionString.Host};Port={connectionString.Port};SslMode=prefer;" +
            $"User Id={connectionString.MasterUser}; Password='{connectionString.MasterPassword}';")) {

            conn.Open();

            // Se crea usuario administrador subapp02...
            string subapp02AdmUsername = connectionString.GetType().GetProperty($"{subapp02Name}AdmUsername").GetValue(connectionString, null);
            string subapp02AdmPassword = connectionString.GetType().GetProperty($"{subapp02Name}AdmPassword").GetValue(connectionString, null);
            try {
                using NpgsqlCommand cmd = new($"CREATE USER \"{subapp02AdmUsername}\" WITH ENCRYPTED PASSWORD '{subapp02AdmPassword}'", conn);
                cmd.ExecuteNonQuery();
            } catch (Exception ex) {
                LambdaLogger.Log("Error al crear usuario administrador de la subapp02: " + ex);
                retorno.Add("Error al crear usuario administrador de la subapp02: " + ex);
            }

            // Se crea database subapp02...
            string subapp02Database = connectionString.GetType().GetProperty($"{subapp02Name}Database").GetValue(connectionString, null);
            try {
                using NpgsqlCommand cmd = new($"CREATE DATABASE \"{subapp02Database}\" OWNER \"{subapp02AdmUsername}\"", conn);
                cmd.ExecuteNonQuery();
            } catch (Exception ex) {
                LambdaLogger.Log("Error al crear base de datos de la subapp02: " + ex);
                retorno.Add("Error al crear base de datos de la subapp02: " + ex);
            }
        }

        return JsonConvert.SerializeObject(retorno);
    }
}
