namespace RobotAutomation.Application.Declarations;

/// <summary>
/// Where a robot gets the figures it has to declare.
///
/// This is the same kind of seam as <see cref="Robots.IRobotPage"/>: it names what the Application needs
/// in domain terms and says nothing about how it is obtained. That matters more than usual here, because
/// the only implementation launches a 32-bit .NET Framework child process to reach an Access database —
/// a constraint that must not leak past this interface.
///
/// <para>Why a child process, in one line: the legacy business layer keeps its state in statics
/// (<c>SocieteBusiness.Regime</c>, <c>ExerciceBusiness.CurrentPeriodeID</c>, <c>Params.FichierT</c>), so
/// two concurrent runs sharing one process would compute one company's declaration with another
/// company's settings — silently, with no error. One process per call is the isolation.</para>
/// </summary>
public interface IDeclarationDataSource
{
    /// <summary>
    /// Reads the declaration for one dossier.
    /// </summary>
    /// <param name="dossierPath">Full path to the client's GénéraFi accounting file (.mdb).</param>
    /// <param name="periodeId">Overrides the dossier's current période; null uses the current one.</param>
    /// <exception cref="InvalidOperationException">The dossier could not be read, or has nothing to
    /// declare. The message is meant for the operator and is surfaced in the run's step log.</exception>
    Task<DeclarationPayload> GetAsync(string dossierPath, int? periodeId, CancellationToken ct);
}
