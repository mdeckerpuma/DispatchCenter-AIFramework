using Domain;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using UsingAIFramework.AI;
using Microsoft.Extensions.Configuration.Json;

internal class Program
{
    static async Task Main(string[] args)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        string connectionString = config["MongoDB:ConnectionString"]!;
        string dbName = config["MongoDB:DatabaseName"]!;

        ConsoleDispatchSink sink = new ConsoleDispatchSink();
        DispatchService main = new DispatchService(sink);
        IncidentRepository repo = new IncidentRepository(connectionString, dbName);
        SummaryRepository summaryrepo = new SummaryRepository(connectionString, dbName);
        SummaryAgent sum = new SummaryAgent(repo,summaryrepo);


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


        await new DispatchAgent(main,repo,sum).RunAsync();
    }
}

