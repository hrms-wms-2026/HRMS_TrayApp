namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.TrayApp.Services;

public sealed class FakeWorkLocationStore : IWorkLocationStore
{
    public WorkLocationReference? Value { get; set; }

    public void Save(WorkLocationReference reference) => Value = reference;

    public WorkLocationReference? Load() => Value;
}
