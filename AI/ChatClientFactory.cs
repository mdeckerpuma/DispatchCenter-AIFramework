using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace UsingAIFramework.AI
{
    // ChatClientFactory centralizes all Azure OpenAI connection logic in one place.
    // Instead of each agent building its own client with hardcoded endpoint and key,
    // they receive a ready-made IChatClient from here.
    // This means changing the model, endpoint, or key only ever happens in one place —
    // swapping AgentDeployment in appsettings.json switches the model with no code change.
    public static class ChatClientFactory
    {
        public static IChatClient Create(IConfiguration config, string deploymentName)
        {
            string endpoint = config["AzureOpenAI:Endpoint"]!;
            string apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
                ?? throw new InvalidOperationException("Set AZURE_OPENAI_API_KEY");

            return new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
                .GetChatClient(deploymentName)
                .AsIChatClient()
                .AsBuilder()
                .UseOpenTelemetry(sourceName: "DispatchDemo", configure: cfg => cfg.EnableSensitiveData = true)
                .Build(); ;
        }
    }
}
