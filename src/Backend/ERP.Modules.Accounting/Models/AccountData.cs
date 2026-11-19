namespace ERP.Modules.Accounting.Models;

public sealed record AccountData(string Code, string Name, AccountKind Kind, NormalBalance NormalBalance, Guid? ParentId, bool AllowsPosting);
