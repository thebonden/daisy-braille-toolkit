namespace DbtSharePointSample;

public sealed record AppConfig(
    string TenantId,
    string ClientId,
    string[] Scopes,
    string SiteUrl,
    ListNames Lists,
    string DateKeyFormat,
    int SequencePadding
);

public sealed record ListNames(
    string Counters,
    string Productions,
    string ProducedFor,
    string ProducedFrom,
    string ReturnAddress,
    string EmployeeAbbrev
);

public sealed record ReserveResult(
    string DateKey,
    int Sequence,
    string VolumeLabel,
    string ProducedForCode
);
