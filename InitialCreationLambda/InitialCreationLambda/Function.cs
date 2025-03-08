using Amazon.Lambda.Core;
using MateoAPI.Helpers;
using Newtonsoft.Json;
using Npgsql;
using System.Diagnostics;

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
        Stopwatch sw = Stopwatch.StartNew();
        LambdaLogger.Log($"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Iniciando proceso de creacion inicial de la base de datos y sus usuarios administradores...");

        string secretArnConnectionString = Environment.GetEnvironmentVariable("SECRET_ARN_CONNECTION_STRING") ?? throw new ArgumentNullException("SECRET_ARN_CONNECTION_STRING");
        string subapp01Name = Environment.GetEnvironmentVariable("SUBAPP_01_NAME") ?? throw new ArgumentNullException("SUBAPP_01_NAME");
        string subapp02Name = Environment.GetEnvironmentVariable("SUBAPP_02_NAME") ?? throw new ArgumentNullException("SUBAPP_02_NAME");

        LambdaLogger.Log($"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Obteniendo secreto de conexion a base de datos...");

        Dictionary<string, string> connectionString = SecretManager.ObtenerSecreto(secretArnConnectionString).Result;

        List<string> retorno = [];

        LambdaLogger.Log($"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Conectandose a base de datos RDS PostgreSQL [{connectionString["Host"]}]...");

        using (NpgsqlConnection conn = new(
            $"Server={connectionString["Host"]};Port={connectionString["Port"]};SslMode=prefer;" +
            $"Database={connectionString["DefaultDatabase"]};User Id={connectionString["MasterUser"]}; Password='{connectionString["MasterPassword"]}';")) {

            conn.Open();

            // Se crea database subapp02...
            string subapp02Database = connectionString[$"{subapp02Name}Database"];
            if (subapp02Database.Contains('"')) {
                throw new Exception($"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Error con el nombre de la base de datos para subapp02 \"{subapp02Name}\" - Caracteres invalidos...");
            }

            LambdaLogger.Log($"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Creando base de datos para subapp02 \"{subapp02Name}\" - [Base de Datos: {subapp02Database}]...");
            try {
                using NpgsqlCommand cmd = new($"CREATE DATABASE \"{subapp02Database}\"", conn);
                cmd.ExecuteNonQuery();
            } catch (Exception ex) {
                string mensaje = $"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Error al crear base de datos de la subapp02: " + ex;
                LambdaLogger.Log(mensaje);
                retorno.Add(mensaje);
            }

            // Se crea usuario administrador subapp02...
            string subapp02AdmUsername = connectionString[$"{subapp02Name}AdmUsername"];
            if (subapp02AdmUsername.Contains('"')) {
                throw new Exception($"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Error con el nombre de usuario administrador para subapp02 \"{subapp02Name}\" - Caracteres invalidos...");
            }
            string subapp02AdmPassword = connectionString[$"{subapp02Name}AdmPassword"];
            if (subapp02AdmPassword.Contains('\'')) {
                throw new Exception($"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Error con la contraseña del usuario administrador para subapp02 \"{subapp02Name}\" - Caracteres invalidos...");
            }

            LambdaLogger.Log($"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Creando usuario administrador para subapp02 \"{subapp02Name}\"...");
            try {
                using NpgsqlCommand cmd = new($"CREATE USER \"{subapp02AdmUsername}\" WITH ENCRYPTED PASSWORD '{subapp02AdmPassword}' CREATEROLE", conn);
                cmd.ExecuteNonQuery();
            } catch (Exception ex) {
                string mensaje = $"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Error al crear usuario administrador de la subapp02: " + ex;
                LambdaLogger.Log(mensaje);
                retorno.Add(mensaje);
            }

            // Se otorgan permisos al nuevo usuario administrador subapp02 sobre la base creada...
            LambdaLogger.Log($"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Otorgando permisos a usuario administrador para subapp02 \"{subapp02Name}\"...");
            try {
                using NpgsqlCommand cmd = new($"GRANT ALL PRIVILEGES ON DATABASE \"{subapp02Database}\" TO \"{subapp02AdmUsername}\" WITH GRANT OPTION", conn);
                cmd.ExecuteNonQuery();
            } catch (Exception ex) {
                string mensaje = $"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Error al otorgar permisos al usuario administrador de la subapp02: " + ex;
                LambdaLogger.Log(mensaje);
                retorno.Add(mensaje);
            }
        }

        LambdaLogger.Log($"[Elapsed Time: {sw.ElapsedMilliseconds} ms] - Ha terminado el proceso de creacion inicial de la base de datos y sus usuarios administradores...");

        return JsonConvert.SerializeObject(retorno);
    }
}
