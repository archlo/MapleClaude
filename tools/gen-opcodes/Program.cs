namespace MapleClaude.Tools.GenOpcodes;

/// <summary>
/// Phase 1 placeholder. Lands on the phase-1/packet-io branch with a real parser
/// that reads the upstream Kinoko In/OutHeader.java files and emits
/// src/MapleClaude.Net/Packet/OpCodes.cs.
///
/// Usage (planned):
///   gen-opcodes --in &lt;path to InHeader.java&gt; --out &lt;path to OutHeader.java&gt; --emit src/MapleClaude.Net/Packet/OpCodes.cs
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("gen-opcodes: phase-1/scaffolding placeholder (no codegen yet)");
        return 0;
    }
}
