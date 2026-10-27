using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public interface IUpdateLogService
{
    void Record(UpdateLogEntry entry);
    IReadOnlyList<UpdateLogEntry> GetAll();
    void Clear();
}
