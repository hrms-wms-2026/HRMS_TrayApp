using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.TrayApp.Services;

/// <summary>Persists the employee's confirmed work location reference across sessions.</summary>
public interface IWorkLocationStore
{
    void Save(WorkLocationReference reference);

    WorkLocationReference? Load();
}
