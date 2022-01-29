﻿using HarmonyLib;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Blueprints.Root.Strings.GameLog;
using Kingmaker.EntitySystem;
using Kingmaker.Enums;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UI.Common;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;
using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityModManagerNet;
using static UnityModManagerNet.UnityModManager.ModEntry;

namespace WOTRDamageBreakdown
{

    public class Main
    {
        public static ModLogger logger;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            logger = modEntry.Logger;
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            

            return true;
        }
    }

    public static class DamageModifiersBreakdown
    {
        private static int CompareModifiers(Modifier x, Modifier y)
        {
            return ModifierDescriptorComparer.Instance.Compare(x.Descriptor, y.Descriptor);
        }

        public static void AppendDamageModifiersBreakdown(this StringBuilder sb, RuleDealDamage rule, int totalBonus, int trueTotal)
        {
            var modifiers = rule.ResultList.SelectMany(r => r.Source.Modifiers).ToList();
            var weapon = rule.DamageBundle.Weapon;
            var damageBonusStat = rule.AttackRoll?.WeaponStats.DamageBonusStat;
            
            if (totalBonus != trueTotal)
            {
                var unitPartWeaponTraining = rule.Initiator.Descriptor.Get<UnitPartWeaponTraining>();
                var weaponTrainingRank = unitPartWeaponTraining?.GetWeaponRank(weapon);
                if (weaponTrainingRank.HasValue && weaponTrainingRank.Value > 0)
                {
                    modifiers.Add(new Modifier(weaponTrainingRank.Value, unitPartWeaponTraining.WeaponTrainings.First(), ModifierDescriptor.None));
                    totalBonus += weaponTrainingRank.Value;
                }
            }

            if (totalBonus != trueTotal)
                modifiers.Add(new Modifier(trueTotal - totalBonus, ModifierDescriptor.UntypedStackable));

            if (modifiers.Count <= 0)
                return;

            modifiers.Sort(new Comparison<Modifier>(CompareModifiers));

            for (var i = 0; i < modifiers.Count; ++i)
            {
                if (modifiers[i].Value != 0)
                {
                    string source;
                    var fact = modifiers[i].Fact;

                    if (i == 0 && damageBonusStat.HasValue)
                    {
                        source = damageBonusStat.Value.ToString();
                    }
                    else if (modifiers[i].Descriptor == ModifierDescriptor.Enhancement && weapon != null)
                    {
                        const string plusPattern = @"\s+\+\d+";
                        var regex = new Regex(plusPattern);
                        source = regex.Replace(weapon.Blueprint.Name, "");
                    }
                    else if (fact != null && fact.GetName().Contains("Weapon Training"))
                    {
                        var parts = fact.GetName().Split(' ');
                        source = $"{string.Join(" ", parts.Take(2))} ({string.Join(" ", parts.Skip(2))})";
                    }
                    else
                    {
                        source = fact?.GetName();
                    }

                    sb.AppendBonus(modifiers[i].Value, source, modifiers[i].Descriptor);
                }
            }
        }

        public static string GetName(this EntityFact fact) {
            var pascalCase = fact.Blueprint?.name ?? fact.GetType().Name;
            pascalCase = pascalCase.Replace("Feature", string.Empty).Replace("Buff", string.Empty).Replace("Effect", string.Empty).Replace("Feat", string.Empty);
            var returnString = pascalCase[0].ToString();

            for (var i = 1; i < pascalCase.Length; ++i)
            {
                if (pascalCase[i].IsUpperCase())
                    returnString += " ";

                returnString += pascalCase[i];
            }

            return returnString;
        }

        public static bool IsUpperCase(this char character)
        {
            return character <= 90 && character >= 65;
        }
    }

    [HarmonyPatch(typeof(DamageLogMessage), nameof(DamageLogMessage.AppendDamageDetails))]
    class DamageLogPatch
    {
        static void Postfix(StringBuilder sb, RuleDealDamage rule)
        {
            var totalBonus = rule.ResultList.Sum(damageValue => damageValue.Source.Modifiers.Sum(m => m.Value));
            int trueTotal = rule.ResultList.Sum(dv => dv.Source.TotalBonus);
            var isZeroDice = rule.ResultList.Any(r => r.Source.Dice.Dice == Kingmaker.RuleSystem.DiceType.Zero || r.Source.Dice.Rolls == 0);
            if (rule != null && trueTotal != 0 && !isZeroDice)
            {
                Main.logger.Log($"{rule.Initiator.CharacterName} has total bonus {trueTotal}, semi total {totalBonus}, summed bonus {rule.ResultList.Sum(dv => dv.Source.Bonus)}");
                sb.Append('\n');
                sb.Append($"<b>Damage bonus: {UIConsts.GetValueWithSign(trueTotal)}</b>\n");
                sb.AppendDamageModifiersBreakdown(rule, totalBonus, trueTotal);
            }
        }
    }
}