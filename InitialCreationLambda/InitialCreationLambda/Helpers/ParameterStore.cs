using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

namespace MateoAPI.Helpers {
    public class ParameterStore {
        public static async Task<string> ObtenerParametro(string parameterArn) {
            IAmazonSimpleSystemsManagement client = new AmazonSimpleSystemsManagementClient();
            GetParameterResponse response = await client.GetParameterAsync(new GetParameterRequest { 
                Name = parameterArn
            });
        
            if (response == null || response.Parameter == null) {
                throw new Exception("No se pudo rescatar correctamente el parámetro");
            }

            return response.Parameter.Value;
        }
    }
}
