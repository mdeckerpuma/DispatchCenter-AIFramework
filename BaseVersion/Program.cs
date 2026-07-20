// ═══════════════════════════════════════════════════════════════════════════
// BASE VERSION (pre-AI) — reference only, excluded from compilation.
//
// This is the dispatch center as a plain menu-driven console app: every action
// is a numbered menu choice and a sequence of typed prompts. No Agent
// Framework, no LLM, no tools. Compare with the root Program.cs, where the
// SAME DispatchCenter domain is driven by natural language through an AIAgent
// with function tools and approval gates.
//
// The domain code (DispatchCenter.cs, Unit.cs, Incident.cs, enums.cs) is
// IDENTICAL between the two versions — only the interaction layer changes.
// That is the point of the comparison.
//
// Excluded from build via <Compile Remove="BaseVersion\**" /> in the csproj
// (two Program classes with Main cannot coexist in one executable).
// ═══════════════════════════════════════════════════════════════════════════

internal class Program

    {

    static DispatchCenter main = new DispatchCenter();

    static void Main(string[] args)

        {

        main.IncidentReported += (_, e) => Console.WriteLine($"\n[INCIDENT] {e.Incident.Id} at {e.Incident.Location}");

        main.UnitDispatched += (_, e) => Console.WriteLine($"\n[DISPATCH] {e.Unit.Id} dispatched to {e.Incident.Location}");

        main.AlertBroadcast += (_, msg) => Console.WriteLine($"\n[ALERT] {msg}");

        Unit police1 = new Unit("POL111", "Ride Combat", UnitType.Police);

        main.AddUnit(police1);

        Unit police2 = new Unit("POL222", "Zombilla", UnitType.Police);

        main.AddUnit(police2);

        Unit ambulance1 = new Unit("AMB111", "Lincroft", UnitType.Ambulance);

        main.AddUnit(ambulance1);

        Unit ambulance2 = new Unit("AMB222", "Oceanport", UnitType.Ambulance);

        main.AddUnit(ambulance2);

        Unit firefighter1 = new Unit("FFR111", "Lincroft", UnitType.Firefighter);

        main.AddUnit(firefighter1);

        Unit firefighter2 = new Unit("FFR222", "Oceanport", UnitType.Firefighter);

        main.AddUnit(firefighter2);


        Console.WriteLine("══════════════════════════════════════════");

        Console.WriteLine("  CITY DISPATCH CENTER");

        Console.WriteLine("══════════════════════════════════════════");

        Console.WriteLine("  1  Report incident");

        Console.WriteLine("  2  Dispatch a unit");

        Console.WriteLine("  3  Mark unit arrived");

        Console.WriteLine("  4  Resolve incident");

        Console.WriteLine("  5  Status board");

        Console.WriteLine("  0  Quit");

        Console.Write("\nSelect: ");

        string Uinput = Console.ReadLine();

        while (Uinput != "0")

        {

            if(Uinput == "1") { ReportIncident(); }

            else if(Uinput == "2") { DisbatchUnit(); }

            else if (Uinput == "3") { MarkArrived(); }

            else if (Uinput == "4") { ResolveIncident(); }

            else if (Uinput == "5") { StatusBoard(); }

            else { Console.WriteLine("Type in a valid input: "); Uinput = Console.ReadLine(); continue; }

            Console.WriteLine("\n══════════════════════════════════════════");

            Console.WriteLine("  CITY DISPATCH CENTER");

            Console.WriteLine("══════════════════════════════════════════");

            Console.WriteLine("  1  Report incident");

            Console.WriteLine("  2  Dispatch a unit");

            Console.WriteLine("  3  Mark unit arrived");

            Console.WriteLine("  4  Resolve incident");

            Console.WriteLine("  5  Status board");

            Console.WriteLine("  0  Quit");

            Console.Write("\nSelect: ");

            Uinput = Console.ReadLine();

        }

        Console.WriteLine("User Quit");

        }

    public static void ReportIncident()

    {

        Console.WriteLine("\nWhat is the kind of Incident? ");

        Console.WriteLine("  1  Medical Emergency");

        Console.WriteLine("  2  Fire");

        Console.WriteLine("  3  Crime In Progress");

        Console.WriteLine("  4  Traffic Incident");

        Console.WriteLine("  5  Hazardous Material");

        Console.Write("\nType number to Select: ");

        string incidentType = Console.ReadLine();

        IncidentType itype = IncidentType.MedicalEmergency;

        bool typedNumber = true;

        while (typedNumber)

        {

            if (incidentType == "1") { itype = IncidentType.MedicalEmergency; break; }

            else if (incidentType == "2") { itype = IncidentType.Fire; break; }

            else if (incidentType == "3") { itype = IncidentType.CrimeInProgress; break; }

            else if (incidentType == "4") { itype = IncidentType.TrafficIncident; break; }

            else if (incidentType == "5") { itype = IncidentType.HazardousMaterial; break; }

            else { Console.WriteLine("Please type a number 1-5: "); incidentType = Console.ReadLine(); }

        }

        Console.WriteLine("\nWhat is the priority? ");

        Console.WriteLine("  1  Low");

        Console.WriteLine("  2  Medium");

        Console.WriteLine("  3  High");

        Console.WriteLine("  4  Critical");

        Console.Write("\nType number to Select: ");

        string incidentPriority = Console.ReadLine();

        IncidentPriority ipriority = IncidentPriority.Low;

        bool typedNumber1 = true;

        while (typedNumber1)

        {

            if (incidentPriority == "1") { ipriority = IncidentPriority.Low; break; }

            else if (incidentPriority == "2") { ipriority = IncidentPriority.Medium; break; }

            else if (incidentPriority == "3") { ipriority = IncidentPriority.High; break; }

            else if (incidentPriority == "4") { ipriority = IncidentPriority.Critical; break; }

            else { Console.WriteLine("Please type a number 1-4: "); incidentPriority = Console.ReadLine(); }

        }

        Console.WriteLine("\nWhere is the location of this Incident?");

        string location = Console.ReadLine();

        Console.WriteLine("\nCan you decribe this Incident?");

        string description = Console.ReadLine();

        Incident i = main.ReportIncident(itype, ipriority, location, description);

        Console.WriteLine($"{i.Id} is this incident's official ID.");

    }

    public static void DisbatchUnit()

    {

        Console.WriteLine("\nAvailable Units:");

        foreach(Unit available in main.GetUnits())

        {

            if(available.Status == UnitStatus.Available) { Console.WriteLine($"Unit Type:{available.Type} ID:{available.Id} is available."); }

        }

        Console.WriteLine("\nActive Incidents:");

        foreach(Incident active in main.GetActiveIncidents()) { Console.WriteLine($"Incident Type:{active.Type} with priority {active.Priority} ID:{active.Id} is currently active."); }

        Console.WriteLine("\nType a Unit Id from the list to disbatch: ");

        string unitId = Console.ReadLine();

        Console.WriteLine("\nType a Incident Id from the list to disbatch: ");

        string incidentId = Console.ReadLine();

        bool disbatch = main.DisbatchUnit(unitId, incidentId);

        if (disbatch)

        {

            Console.WriteLine("Unit succsessfully disbatched.");

        }

        else

        {

            Console.WriteLine("Failed. Taking back to main menu to try again.");

        }

    }

    public static void MarkArrived()

    {

        Console.WriteLine("\nDisbatched Units:");

        foreach (Unit available in main.GetUnits())

        {

            if (available.Status == UnitStatus.Dispatched) { Console.WriteLine($"Unit Type:{available.Type} ID:{available.Id} is disbatched."); }

        }

        Console.WriteLine("\nType a Unit Id from the list to mark arrived: ");

        string unitId = Console.ReadLine();

        bool arrived = main.MarkArrived(unitId);

        if (arrived)

        {

            Console.WriteLine("Unit succsessfully marked arrived.");

        }

        else

        {

            Console.WriteLine("Failed. Taking back to main menu to try again.");

        }

    }

    public static void ResolveIncident()

    {

        Console.WriteLine("\nActive Incidents:");

        foreach (Incident active in main.GetActiveIncidents()) { Console.WriteLine($"Incident Type:{active.Type} with priority {active.Priority} ID:{active.Id} is currently active."); }

        Console.WriteLine("\nType a Incident Id from the list to Resolve: ");

        string incidentId = Console.ReadLine();

        bool resolve = main.ResolveIncident(incidentId);

        if (resolve)

        {

            Console.WriteLine("Incident succsessfully resolved.");

        }

        else

        {

            Console.WriteLine("Failed. Taking back to main menu to try again.");

        }

    }

    public static void StatusBoard()

    {

        Console.WriteLine("\nActive Incidents:");

        foreach (Incident active in main.GetActiveIncidents()) {

            Console.WriteLine($"  [{active.Priority}] {active.Id} — {active.Type} at {active.Location} ({active.Status})");

        }

        Console.WriteLine("\nUnits:");

        foreach (Unit available in main.GetUnits())

        {

            if(available.AssignIncidentId != null)

            {

                Console.WriteLine($"  [{available.Status}] {available.Id} — {available.Name} ({available.Type}) → {available.AssignIncidentId}");

            }

            else

            {

                Console.WriteLine($"  [AVAILABLE] {available.Id} — {available.Name} ({available.Type})");

            }

        }

    }

}
