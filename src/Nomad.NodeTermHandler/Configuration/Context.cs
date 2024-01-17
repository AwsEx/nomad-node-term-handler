using Flurl.Http;
using Newtonsoft.Json;

namespace Nomad.NodeTermHandler.Configuration
{
    public class Context
    {
        public static string Application => "nomad-node-term-handler";
        public static string? Account => System.Environment.GetEnvironmentVariable("CONTEXT_ACCOUNT");
        public static string Region => System.Environment.GetEnvironmentVariable("CONTEXT_REGION") ?? "us-east-1";
        public static string? AccountId => GetAccountIdLazy.Value.GetAwaiter().GetResult();

        private static readonly Lazy<Task<string?>> GetAccountIdLazy = new(GetAccountId);
        private static async Task<string?> GetAccountId()
        {
            try
            {
                IFlurlResponse r;
                try
                {
                    r = await "http://169.254.169.254/latest/dynamic/instance-identity/document".WithTimeout(1).GetAsync();
                }
                catch (FlurlHttpException ex) when (ex.StatusCode == StatusCodes.Status401Unauthorized)
                {
                    var t = await "http://169.254.169.254/latest/api/token".WithHeader("X-aws-ec2-metadata-token-ttl-seconds", 21600).WithTimeout(1).PutAsync();
                    r = await "http://169.254.169.254/latest/dynamic/instance-identity/document".WithHeader("X-aws-ec2-metadata-token", await t.GetStringAsync()).WithTimeout(1).GetAsync();
                }

                var o = JsonConvert.DeserializeAnonymousType(await r.GetStringAsync(), new { accountId = (string?) null });
                return o?.accountId;
            }
            catch
            {
                return null;
            }
        }
    }
}
