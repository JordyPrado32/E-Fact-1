using Simetric.Services;

var service = new InitialSequencePromptService(null!, null!);
var guide = new InitialSequencePromptState { Initialized = true, HadPreviousDocuments = true, PreviousSequence = "000000100" };
var debit = new InitialSequencePromptState { Initialized = true, HadPreviousDocuments = true, PreviousSequence = "000000050" };

Check(service.ResolveNextSequence("000000001", guide, preserveConfiguredStart: true), "000000101");
Check(service.ResolveNextSequence("000000120", guide, preserveConfiguredStart: true), "000000120");
Check(service.ResolveFirstAvailableSequence(new[] { "000000001", "000000002" }, debit, preserveConfiguredStart: true), "000000051");
Check(service.ResolveFirstAvailableSequence(new[] { "000000051" }, debit, preserveConfiguredStart: true), "000000052");
Check(service.ResolveNextSequence("000000001", guide), "000000001");

guide.PreviousSequence = "999999999";
Check(service.ResolveNextSequence("000000001", guide, preserveConfiguredStart: true), string.Empty);
Check(service.ResolveFirstAvailableSequence(Array.Empty<string>(), guide, preserveConfiguredStart: true), string.Empty);
Console.WriteLine("Secuencias maestras independientes: 7 comprobaciones correctas.");

static void Check(string actual, string expected)
{
    if (actual != expected)
        throw new InvalidOperationException($"Secuencia esperada {expected}, obtenida {actual}.");
}
