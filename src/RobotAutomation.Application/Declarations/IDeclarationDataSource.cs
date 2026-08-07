namespace RobotAutomation.Application.Declarations;

public interface IDeclarationDataSource
{
    /// <param name="dossierPath">Full path to the client's GénéraFi accounting file (.mdb).</param>
    /// <param name="periodeId">Overrides the dossier's current période; null uses the current one.</param>
    Task<DeclarationPayload> GetAsync(string dossierPath, int? periodeId, CancellationToken ct);
}
