using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Mechanisms;
using RimMind.Application.Common.Models.Mechanisms;
using RimMind.Domain.Enums;
using RimMind.Domain.ValueObjects;
using Verse;
using VerseMap = Verse.Map;

namespace RimMind.Infrastructure.Mechanisms
{
    /// <summary>
    /// 泛型 Mechanism 基类，继承 <see cref="GameMechanismBaseNoDef"/>，
    /// 仅 override <see cref="ExecuteListAsync"/> 以枚举 <see cref="DefDatabase{TDef}"/>。
    /// 其余共享逻辑（IExtension 成员、Execute*Async 默认实现、FindPawn、ResolveMap）
    /// 均由 <see cref="GameMechanismBaseNoDef"/> 提供，消除约 67 行重复代码。
    /// </summary>
    /// <typeparam name="TDef">枚举的 Def 类型，用于 ExecuteListAsync。</typeparam>
    public abstract class GameMechanismBase<TDef> : GameMechanismBaseNoDef
        where TDef : Def, new()
    {
        /// <summary>
        /// 枚举 <see cref="DefDatabase{TDef}"/> 中所有 Def 作为可选项。
        /// 非 Def 类型的 Mechanism（继承 NoDef 直接）保持返回 Err 的默认行为。
        /// </summary>
        public override Task<Result<IReadOnlyList<MechanismEnumResult>, RimMindError>> ExecuteListAsync(int? pawnId, CancellationToken ct)
        {
            var results = DefDatabase<TDef>.AllDefsListForReading
                .Select(d => new MechanismEnumResult
                {
                    DefName = d.defName,
                    Label = d.label ?? d.defName,
                    Description = d.description
                })
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<MechanismEnumResult>, RimMindError>.Ok(results.AsReadOnly()));
        }

        /// <summary>
        /// 按 defName 查找 <typeparamref name="TDef"/>。仅 Def 类型 Mechanism 需要。
        /// </summary>
        protected static TDef? FindDef(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            return DefDatabase<TDef>.GetNamed(defName);
        }

        /// <summary>
        /// 验证地图非空，否则返回 <see cref="RimMindErrors.MapNotFound"/> 错误。
        /// 仅 Def 类型 Mechanism 需要。
        /// </summary>
        protected static Result<T, RimMindError> ValidateMapOrErr<T>(VerseMap? map)
        {
            if (map == null)
                return Result<T, RimMindError>.Err(RimMindErrors.MapNotFound(0));
            return Result<T, RimMindError>.Ok(default!);
        }
    }
}
