using Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using UsingAIFramework.AI;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Azure.Monitor.OpenTelemetry.Exporter;

// Composition root. Deliberately dull: build telemetry, read config, create the one
// IChatClient both agents share, wire the domain, seed the roster, hand off to the agent.
// Changing model or provider is a change to ChatClientFactory alone — nothing below it
// knows which LLM is behind the IChatClient.
internal class Program
{
    static async Task Main(string[] args)
    {
        // Tracing is set up before anything else so the IChatClient built below, which is
        // wrapped in UseOpenTelemetry, has a live provider to emit spans into.
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
           .AddSource("DispatchDemo")
           .AddAzureMonitorTraceExporter()
           .Build();

        // Key-based Azure OpenAI auth (DefaultAzureCredential fails in our environment).
        // Set AZURE_OPENAI_API_KEY in your environment before running.
        string apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? throw new InvalidOperationException("Set AZURE_OPENAI_API_KEY before running.");

        // #2/#3  Build each provider as the SAME interface (IChatClient), then wrap them
        //        so the active one can be swapped at runtime. The agent is built ONCE over
        //        the wrapper; swapping the model keeps the conversation memory because
        //        history lives in the session, not the model.
        IChatClient azure = new AzureOpenAIClient(
                new Uri("https://squidopenai.openai.azure.com/"),
                new AzureKeyCredential(apiKey))
            .GetChatClient("gpt-4o-mini")
            .AsIChatClient();

        IChatClient ollama = new OllamaApiClient(new Uri("http://localhost:11434"), "llama3.2");

        clientSwitch = new SwitchableChatClient("azure", azure);
        clientSwitch.Register("ollama", ollama);

        // Claude — ready for fold-in. Set ANTHROPIC_API_KEY, then uncomment these lines:
        // string? claudeKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        // if (!string.IsNullOrEmpty(claudeKey))
        //     clientSwitch.Register("claude",
        //         new Anthropic.AnthropicClient(claudeKey).AsIChatClient("claude-sonnet-4-5"));

        AIAgent agent = clientSwitch
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = "DispatchAgent",
                // #4  Context storage/retrieval: this provider injects the live dispatch
                //     state into the model before EVERY turn, so the agent can answer
                //     "what is POL111 doing?" from retrieved context without a tool call.
                AIContextProviders = new List<AIContextProvider> { new DispatchContextProvider(BuildStateSnapshot) },
                ChatOptions = new()
                {
                    Instructions = "You are a city dispatch center assistant. Use GetStatus first to find current unit and incident IDs before dispatching or resolving. Always use exact IDs. After you report an incident you MAY offer to dispatch an appropriate available unit (report then dispatch is the ONE allowed chain); do not chain any other actions. For everything else, do only the single action the user explicitly asks for. If you just proposed a tool call and the user declined it, do not re-propose that same call again in that same turn; acknowledge and wait. But if the user later explicitly asks for that action again in a new message, go ahead and propose it.",
                    Tools =
                    [
                        AIFunctionFactory.Create(GetStatus),
                        AIFunctionFactory.Create(switch_model),
                        // Recall is exposed so the AI CAN attempt it, but the gate hard-blocks
                        // every AI attempt — recall is human-only via /authorize recall.
                        AIFunctionFactory.Create(RecallAllUnitsFromIncident),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ReportIncident)),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DispatchUnit)),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(MarkArrived)),
                        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ResolveIncident)),
                    ]
                }
            })
            .AsBuilder()
            .Use(LoggingMiddleware)      // #5 run middleware: prints debug info each turn
            .Use(FunctionGateMiddleware) // #6 function-calling middleware: allow-list + arg validation
            .Build();

        bool debug = args.Contains("--verbose");

        string connectionString = config["MongoDB:ConnectionString"]!;
        string dbName = config["MongoDB:DatabaseName"]!;
        string agentDeployment = config["AzureOpenAI:AgentDeployment"]!;
        IChatClient chatClient = ChatClientFactory.Create(config, agentDeployment);

        // One chat client, two agents. Both therefore run on the same deployment and the
        // same telemetry pipeline, and the domain below is constructed the same way the
        // menu-driven BaseVersion constructs it.
        ConsoleDispatchSink sink = new ConsoleDispatchSink();
        DispatchService main = new DispatchService(sink);
        IncidentRepository repo = new IncidentRepository(connectionString, dbName);
        SummaryRepository summaryrepo = new SummaryRepository(connectionString, dbName);
        SummaryAgent sum = new SummaryAgent(repo,summaryrepo,chatClient);


        // Fixed roster, in memory, recreated identically every run. Units are never
        // persisted — only incidents and their assignments are — so a restart rebuilds the
        // fleet and then replays statuses onto it from Mongo in DispatchAgent.RunAsync.
        Unit police1 = new Unit("POL001", "Eagle One", UnitType.Police);
        Unit police2 = new Unit("POL002", "Iron Fist", UnitType.Police);
        Unit police3 = new Unit("POL003", "Shadow Unit", UnitType.Police);
        Unit police4 = new Unit("POL004", "Night Watch", UnitType.Police);
        Unit police5 = new Unit("POL005", "Steel Ridge", UnitType.Police);
        Unit police6 = new Unit("POL006", "Cobra Six", UnitType.Police);
        Unit police7 = new Unit("POL007", "Falcon Blue", UnitType.Police);
        Unit police8 = new Unit("POL008", "Viper Squad", UnitType.Police);
        Unit police9 = new Unit("POL009", "Apex Unit", UnitType.Police);
        Unit police10 = new Unit("POL010", "Delta Force", UnitType.Police);
        Unit police11 = new Unit("POL011", "Thunder Road", UnitType.Police);
        Unit police12 = new Unit("POL012", "Ghost Rider", UnitType.Police);
        Unit police13 = new Unit("POL013", "Black Bear", UnitType.Police);
        Unit police14 = new Unit("POL014", "Stone Wall", UnitType.Police);
        Unit police15 = new Unit("POL015", "Red Hawk", UnitType.Police);
        Unit police16 = new Unit("POL016", "Cold Steel", UnitType.Police);
        Unit police17 = new Unit("POL017", "Lone Wolf", UnitType.Police);

        Unit ambulance1 = new Unit("AMB001", "Lincroft Med", UnitType.Ambulance);
        Unit ambulance2 = new Unit("AMB002", "Oceanport Med", UnitType.Ambulance);
        Unit ambulance3 = new Unit("AMB003", "Red Cross Alpha", UnitType.Ambulance);
        Unit ambulance4 = new Unit("AMB004", "Lifeline One", UnitType.Ambulance);
        Unit ambulance5 = new Unit("AMB005", "Rescue Seven", UnitType.Ambulance);
        Unit ambulance6 = new Unit("AMB006", "Mercy Unit", UnitType.Ambulance);
        Unit ambulance7 = new Unit("AMB007", "Swift Care", UnitType.Ambulance);
        Unit ambulance8 = new Unit("AMB008", "Vital Response", UnitType.Ambulance);
        Unit ambulance9 = new Unit("AMB009", "Code Blue", UnitType.Ambulance);
        Unit ambulance10 = new Unit("AMB010", "Heartbeat One", UnitType.Ambulance);
        Unit ambulance11 = new Unit("AMB011", "Trauma Team", UnitType.Ambulance);
        Unit ambulance12 = new Unit("AMB012", "Bay Shore Med", UnitType.Ambulance);
        Unit ambulance13 = new Unit("AMB013", "Highland Care", UnitType.Ambulance);
        Unit ambulance14 = new Unit("AMB014", "Rapid Pulse", UnitType.Ambulance);
        Unit ambulance15 = new Unit("AMB015", "Clear Path", UnitType.Ambulance);
        Unit ambulance16 = new Unit("AMB016", "First Response", UnitType.Ambulance);

        Unit firefighter1 = new Unit("FFR001", "Lincroft Fire", UnitType.Firefighter);
        Unit firefighter2 = new Unit("FFR002", "Oceanport Fire", UnitType.Firefighter);
        Unit firefighter3 = new Unit("FFR003", "Blaze Breaker", UnitType.Firefighter);
        Unit firefighter4 = new Unit("FFR004", "Inferno Squad", UnitType.Firefighter);
        Unit firefighter5 = new Unit("FFR005", "Red Ladder", UnitType.Firefighter);
        Unit firefighter6 = new Unit("FFR006", "Smoke Jumper", UnitType.Firefighter);
        Unit firefighter7 = new Unit("FFR007", "Phoenix One", UnitType.Firefighter);
        Unit firefighter8 = new Unit("FFR008", "Ember Watch", UnitType.Firefighter);
        Unit firefighter9 = new Unit("FFR009", "Torch Break", UnitType.Firefighter);
        Unit firefighter10 = new Unit("FFR010", "Hot Zone Alpha", UnitType.Firefighter);
        Unit firefighter11 = new Unit("FFR011", "Ladder Seven", UnitType.Firefighter);
        Unit firefighter12 = new Unit("FFR012", "Flash Point", UnitType.Firefighter);
        Unit firefighter13 = new Unit("FFR013", "Ash Rider", UnitType.Firefighter);
        Unit firefighter14 = new Unit("FFR014", "Burn Control", UnitType.Firefighter);
        Unit firefighter15 = new Unit("FFR015", "Iron Gate", UnitType.Firefighter);
        Unit firefighter16 = new Unit("FFR016", "Fire Wall", UnitType.Firefighter);
        Unit firefighter17 = new Unit("FFR017", "Blaze Alpha", UnitType.Firefighter);

        main.AddUnit(police1); main.AddUnit(police2); main.AddUnit(police3);
        main.AddUnit(police4); main.AddUnit(police5); main.AddUnit(police6);
        main.AddUnit(police7); main.AddUnit(police8); main.AddUnit(police9);
        main.AddUnit(police10); main.AddUnit(police11); main.AddUnit(police12);
        main.AddUnit(police13); main.AddUnit(police14); main.AddUnit(police15);
        main.AddUnit(police16); main.AddUnit(police17);

        main.AddUnit(ambulance1); main.AddUnit(ambulance2); main.AddUnit(ambulance3);
        main.AddUnit(ambulance4); main.AddUnit(ambulance5); main.AddUnit(ambulance6);
        main.AddUnit(ambulance7); main.AddUnit(ambulance8); main.AddUnit(ambulance9);
        main.AddUnit(ambulance10); main.AddUnit(ambulance11); main.AddUnit(ambulance12);
        main.AddUnit(ambulance13); main.AddUnit(ambulance14); main.AddUnit(ambulance15);
        main.AddUnit(ambulance16);

        main.AddUnit(firefighter1); main.AddUnit(firefighter2); main.AddUnit(firefighter3);
        main.AddUnit(firefighter4); main.AddUnit(firefighter5); main.AddUnit(firefighter6);
        main.AddUnit(firefighter7); main.AddUnit(firefighter8); main.AddUnit(firefighter9);
        main.AddUnit(firefighter10); main.AddUnit(firefighter11); main.AddUnit(firefighter12);
        main.AddUnit(firefighter13); main.AddUnit(firefighter14); main.AddUnit(firefighter15);
        main.AddUnit(firefighter16); main.AddUnit(firefighter17);


        await new DispatchAgent(main,repo,sum,chatClient,debug).RunAsync();
    }
}

