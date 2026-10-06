namespace Makelazy.App.Models;

public sealed record MakefileTarget(
    string Name,
    string[] Dependencies,
    string[] Commands,
    int Line,
    string Description,
    bool IsPhony);
