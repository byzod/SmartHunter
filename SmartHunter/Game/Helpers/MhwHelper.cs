﻿using SmartHunter.Core.Helpers;
using SmartHunter.Game.Data;
using SmartHunter.Game.Data.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SmartHunter.Game.Helpers
{
    public static class MhwHelper
    {
        // TODO: Wouldn't it be nice if all this were data driven?
        private static class DataOffsets
        {
            public static class Monster
            {
                // Doubly linked list
                public static readonly ulong PreviousMonsterPtr = 0x28;
                public static readonly ulong NextMonsterPtr = 0x30;
                public static readonly ulong SizeScale = 0x174;
                public static readonly ulong ModelPtr = 0x290;
                public static readonly ulong PartCollection = 0x129D8;
                public static readonly ulong RemovablePartCollection = PartCollection + 0x1ED0;
                public static readonly ulong StatusEffectCollection = 0x19870;
            }

            public static class MonsterModel
            {
                public static readonly int IdLength = 64;
                public static readonly ulong Id = 0x0C;
            }

            public static class MonsterHealthComponent
            {
                public static readonly ulong MaxHealth = 0x60;
                public static readonly ulong CurrentHealth = 0x64;
            }

            public static class MonsterPartCollection
            {
                public static readonly ulong HealthComponentPtr = 0x48;
                public static readonly ulong FirstPart = 0x50;
            }

            public static class MonsterPart
            {
                public static readonly ulong MaxHealth = 0x0C;
                public static readonly ulong CurrentHealth = 0x10;
                public static readonly ulong RemoveableState = 0x14;
                public static readonly ulong TimesBrokenCount = 0x18;
                public static readonly ulong NextPart = 0x1E8;
            }

            public static class MonsterRemovablePartCollection
            {
                public static readonly int MaxItemCount = 32;
                public static readonly ulong FirstRemovablePart = 0x08;
            }

            public static class MonsterRemovablePart
            {
                public static readonly ulong MaxHealth = 0x0C;
                public static readonly ulong CurrentHealth = 0x10;
                public static readonly ulong RemovableState = 0x14;
                public static readonly ulong TimesBrokenCount = 0x18;
                public static readonly ulong NextRemovablePart = 0x78;
            }

            public static class MonsterStatusEffectCollection
            {
                public static int MaxItemCount = 20;
                public static ulong NextStatusEffectPtr = 0x08;
            }

            public static class MonsterStatusEffect
            {
                public static readonly ulong Id = 0x158;
                public static readonly ulong MaxDuration = 0x15C;
                public static readonly ulong CurrentBuildup = 0x178;
                public static readonly ulong MaxBuildup = 0x17C;
                public static readonly ulong CurrentDuration = 0x1A4;
                public static readonly ulong TimesActivatedCount = 0x1A8;
            }

            public static class PlayerNameCollection
            {
                public static readonly int PlayerNameLength = 32 + 1; // +1 for null terminator
                public static readonly ulong FirstPlayerName = 0x54A45;
            }

            public static class PlayerDamageCollection
            {
                public static readonly int MaxPlayerCount = 4;
                public static readonly ulong FirstPlayerPtr = 0x48;
                public static readonly ulong NextPlayerPtr = 0x58;
            }

            public static class PlayerDamage
            {
                public static readonly ulong Damage = 0x48;
            }
        }

        private enum RemovablePartState
        {
            EmptyPartOrNotRemovable = -1,
            Invalid = 0,
            ValidPartAndRemovable = 1,
            UnknownMeaning = 10
        }

        private static RemovablePartState GetRemovablePartState(int removablePartState)
        {
            if (removablePartState == -1)
            {
                return RemovablePartState.EmptyPartOrNotRemovable;
            }
            else if (removablePartState == 0)
            {
                return RemovablePartState.Invalid;
            }
            else if (removablePartState == 1)
            {
                return RemovablePartState.ValidPartAndRemovable;
            }
            else if (removablePartState == 10)
            {
                return RemovablePartState.UnknownMeaning;
            }

            return RemovablePartState.EmptyPartOrNotRemovable;
        }

        public static void UpdatePlayerWidget(Process process, ulong statusEffectAddress, ulong equipmentAddress)
        {
            for (int index = 0; index < ConfigHelper.PlayerData.Values.PlayerStatusEffects.Length; ++index)
            {
                var playerStatusEffectConfig = ConfigHelper.PlayerData.Values.PlayerStatusEffects[index];

                ulong sourceAddress = statusEffectAddress;
                if (playerStatusEffectConfig.Source == Config.PlayerStatusEffectConfig.MemorySource.Equipment)
                {
                    sourceAddress = equipmentAddress;
                }

                bool isConditionPassed = true;
                if (playerStatusEffectConfig.Condition != null)
                {
                    ulong conditionOffset;
                    if (ulong.TryParse(playerStatusEffectConfig.Condition.Offset, System.Globalization.NumberStyles.HexNumber, null, out conditionOffset))
                    {
                        var conditionValue = MemoryHelper.Read<int>(process, sourceAddress + conditionOffset);
                        if (conditionValue != playerStatusEffectConfig.Condition.Value)
                        {
                            isConditionPassed = false;
                        }
                    }
                }

                float? timer = null;
                if (isConditionPassed && playerStatusEffectConfig.TimerOffset != null)
                {
                    ulong timerOffset;
                    if (ulong.TryParse(playerStatusEffectConfig.TimerOffset, System.Globalization.NumberStyles.HexNumber, null, out timerOffset))
                    {
                        timer = MemoryHelper.Read<float>(process, sourceAddress + timerOffset);
                    }

                    if (timer <= 0)
                    {
                        timer = 0;
                        isConditionPassed = false;
                    }
                }
                
                OverlayViewModel.Instance.PlayerWidget.Context.UpdateAndGetPlayerStatusEffect(index, timer, isConditionPassed);
            }
        }

        public static void UpdateTeamWidget(Process process, ulong playerDamageCollectionAddress, ulong playerNameCollectionAddress, int localPlayerIndex)
        {
            List<Player> updatedPlayers = new List<Player>();
            for (int playerIndex = 0; playerIndex < DataOffsets.PlayerDamageCollection.MaxPlayerCount; ++playerIndex)
            {
                var player = UpdateAndGetPlayer(process, playerIndex, playerDamageCollectionAddress, playerNameCollectionAddress, playerIndex == localPlayerIndex);
                if (player != null)
                {
                    updatedPlayers.Add(player);
                }
            }

            if (updatedPlayers.Any())
            {
                OverlayViewModel.Instance.TeamWidget.Context.UpdateFractions();
            }
            else if (OverlayViewModel.Instance.TeamWidget.Context.Players.Any())
            {
                OverlayViewModel.Instance.TeamWidget.Context.Players.Clear();
            }
        }

        private static Player UpdateAndGetPlayer(Process process, int playerIndex, ulong playerDamageCollectionAddress, ulong playerNameCollectionAddress, bool isLocalPlayer)
        {
            Player player = null;

            var playerNameOffset = (ulong)DataOffsets.PlayerNameCollection.PlayerNameLength * (ulong)playerIndex;
            string name = MemoryHelper.ReadString(process, playerNameCollectionAddress + DataOffsets.PlayerNameCollection.FirstPlayerName + playerNameOffset, (uint)DataOffsets.PlayerNameCollection.PlayerNameLength);
            ulong firstPlayerPtr = playerDamageCollectionAddress + DataOffsets.PlayerDamageCollection.FirstPlayerPtr;
            ulong currentPlayerPtr = firstPlayerPtr + ((ulong)playerIndex * DataOffsets.PlayerDamageCollection.NextPlayerPtr);
            ulong currentPlayerAddress = MemoryHelper.Read<ulong>(process, currentPlayerPtr);
            int damage = MemoryHelper.Read<int>(process, currentPlayerAddress + DataOffsets.PlayerDamage.Damage);

            if (!String.IsNullOrEmpty(name) || damage > 0)
            {
                player = OverlayViewModel.Instance.TeamWidget.Context.UpdateAndGetPlayer(playerIndex, name, damage, isLocalPlayer);
            }

            return player;
        }

        public static void UpdateMonsterWidget(Process process, ulong lastMonsterAddress)
        {
            if (lastMonsterAddress < 0xffffff)
            {
                OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Clear();
                return;
            }

            List<ulong> monsterAddresses = new List<ulong>();
            
            ulong currentMonsterAddress = lastMonsterAddress;
            while (currentMonsterAddress != 0)
            {
                monsterAddresses.Insert(0, currentMonsterAddress);
                currentMonsterAddress = MemoryHelper.Read<ulong>(process, currentMonsterAddress + DataOffsets.Monster.PreviousMonsterPtr);
            }
            
            List<Monster> updatedMonsters = new List<Monster>();
            foreach (var monsterAddress in monsterAddresses)
            {
                var monster = UpdateAndGetMonster(process, monsterAddress);
                if (monster != null)
                {
                    updatedMonsters.Add(monster);
                }
            }

            // Clean out monsters that aren't in the linked list anymore
            var obsoleteMonsters = OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Except(updatedMonsters);
            foreach (var obsoleteMonster in obsoleteMonsters.Reverse())
            {
                OverlayViewModel.Instance.MonsterWidget.Context.Monsters.Remove(obsoleteMonster);
            }
        }

        private static Monster UpdateAndGetMonster(Process process, ulong monsterAddress)
        {
            Monster monster = null;

            ulong modelPtr = MemoryHelper.Read<ulong>(process, monsterAddress + DataOffsets.Monster.ModelPtr);
            string id = MemoryHelper.ReadString(process, modelPtr + DataOffsets.MonsterModel.Id, (uint)DataOffsets.MonsterModel.IdLength);
            id = id.Split('\\').Last();

            if (String.IsNullOrEmpty(id))
            {
                return monster;
            }

            bool isIncluded = ConfigHelper.Main.Values.Overlay.MonsterWidget.MatchIncludeMonsterIdRegex(id);
            if (!isIncluded)
            {
                return monster;
            }

            ulong healthComponentAddress = MemoryHelper.Read<ulong>(process, monsterAddress + DataOffsets.Monster.PartCollection + DataOffsets.MonsterPartCollection.HealthComponentPtr);
            float maxHealth = MemoryHelper.Read<float>(process, healthComponentAddress + DataOffsets.MonsterHealthComponent.MaxHealth);
            if (maxHealth <= 0)
            {
                return monster;
            }

            float currentHealth = MemoryHelper.Read<float>(process, healthComponentAddress + DataOffsets.MonsterHealthComponent.CurrentHealth);
            float sizeScale = MemoryHelper.Read<float>(process, monsterAddress + DataOffsets.Monster.SizeScale);

            monster = OverlayViewModel.Instance.MonsterWidget.Context.UpdateAndGetMonster(monsterAddress, id, maxHealth, currentHealth, sizeScale);

            UpdateMonsterParts(process, monster);
            UpdateMonsterRemovableParts(process, monster);
            UpdateMonsterStatusEffects(process, monster);

            return monster;
        }

        private static void UpdateMonsterParts(Process process, Monster monster)
        {
            if (monster.Parts.Any())
            {
                foreach (var part in monster.Parts)
                {
                    UpdateMonsterPart(process, monster, part.Address);
                }
            }
            else
            {
                ulong firstPartAddress = monster.Address + DataOffsets.Monster.PartCollection + DataOffsets.MonsterPartCollection.FirstPart;

                int partIndex = 0;
                while (true)
                {
                    ulong currentPartOffset = DataOffsets.MonsterPart.NextPart * (ulong)partIndex;
                    ulong currentPartAddress = firstPartAddress + currentPartOffset;

                    float maxHealth = MemoryHelper.Read<float>(process, currentPartAddress + DataOffsets.MonsterPart.MaxHealth);

                    // Read until we reach an element that has a max health of 0, which is presumably the end of the collection
                    if (maxHealth > 0)
                    {
                        UpdateMonsterPart(process, monster, currentPartAddress);
                    }
                    else
                    {
                        break;
                    }

                    partIndex++;
                }
            }
        }

        private static void UpdateMonsterPart(Process process, Monster monster, ulong partAddress)
        {
            float maxHealth = MemoryHelper.Read<float>(process, partAddress + DataOffsets.MonsterPart.MaxHealth);
            float currentHealth = MemoryHelper.Read<float>(process, partAddress + DataOffsets.MonsterPart.CurrentHealth);
            int removableState = MemoryHelper.Read<int>(process, partAddress + DataOffsets.MonsterPart.RemoveableState);
            int timesBrokenCount = MemoryHelper.Read<int>(process, partAddress + DataOffsets.MonsterPart.TimesBrokenCount);
            bool isRemovable = GetRemovablePartState(removableState) == RemovablePartState.ValidPartAndRemovable;

            monster.UpdateAndGetPart(partAddress, isRemovable, maxHealth, currentHealth, timesBrokenCount);
        }

        private static void UpdateMonsterRemovableParts(Process process, Monster monster)
        {
            if (monster.RemovableParts.Any())
            {
                foreach (var removablePart in monster.RemovableParts)
                {
                    UpdateMonsterRemovablePart(process, monster, removablePart.Address);
                }
            }
            else
            {
                ulong removablePartAddress = monster.Address + DataOffsets.Monster.RemovablePartCollection + DataOffsets.MonsterRemovablePartCollection.FirstRemovablePart;
                for (int index = 0; index < DataOffsets.MonsterRemovablePartCollection.MaxItemCount; ++index)
                {
                    // Every 16 elements there seems to be a new removable part collection. When we reach this point,
                    // we advance past the first 64 bit field to get to the start of the next part again
                    ulong staticPtr = MemoryHelper.Read<ulong>(process, removablePartAddress);
                    if (staticPtr <= 10)
                    {
                        removablePartAddress += 8;
                    }

                    int removableState = MemoryHelper.Read<int>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.RemovableState);

                    bool isRemovable = GetRemovablePartState(removableState) == RemovablePartState.ValidPartAndRemovable;
                    if (isRemovable)
                    {
                        float maxHealth = MemoryHelper.Read<float>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.MaxHealth);
                        if (maxHealth != 0)
                        {
                            UpdateMonsterRemovablePart(process, monster, removablePartAddress);
                        }
                        else
                        {
                            break;
                        }
                    }

                    removablePartAddress += DataOffsets.MonsterRemovablePart.NextRemovablePart;
                }
            }
        }

        private static void UpdateMonsterRemovablePart(Process process, Monster monster, ulong removablePartAddress)
        {
            int removableState = MemoryHelper.Read<int>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.RemovableState);
            bool isRemovable = GetRemovablePartState(removableState) == RemovablePartState.ValidPartAndRemovable;
            float maxHealth = MemoryHelper.Read<float>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.MaxHealth);
            float currentHealth = MemoryHelper.Read<float>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.CurrentHealth);
            int timesBrokenCount = MemoryHelper.Read<int>(process, removablePartAddress + DataOffsets.MonsterRemovablePart.TimesBrokenCount);

            monster.UpdateAndGetPart(removablePartAddress, isRemovable, maxHealth, currentHealth, timesBrokenCount);
        }

        private static void UpdateMonsterStatusEffects(Process process, Monster monster)
        {
            if (monster.StatusEffects.Any())
            {
                foreach (var statusEffect in monster.StatusEffects)
                {
                    UpdateStatusEffect(process, monster, statusEffect.Address);
                }
            }
            else
            {
                ulong statusEffectCollectionAddress = monster.Address + DataOffsets.Monster.StatusEffectCollection;

                for (int index = 0; index < DataOffsets.MonsterStatusEffectCollection.MaxItemCount; ++index)
                {
                    ulong statusEffectPtr = statusEffectCollectionAddress + ((ulong)index * DataOffsets.MonsterStatusEffectCollection.NextStatusEffectPtr);
                    ulong statusEffectAddress = MemoryHelper.Read<ulong>(process, statusEffectPtr);

                    float maxDuration = MemoryHelper.Read<float>(process, statusEffectAddress + DataOffsets.MonsterStatusEffect.MaxDuration);
                    float maxBuildup = MemoryHelper.Read<float>(process, statusEffectAddress + DataOffsets.MonsterStatusEffect.MaxBuildup);
                    if (maxDuration > 0 || maxBuildup > 0)
                    {
                        UpdateStatusEffect(process, monster, statusEffectAddress);
                    }
                }
            }            
        }

        private static void UpdateStatusEffect(Process process, Monster monster, ulong statusEffectAddress)
        {
            int id = MemoryHelper.Read<int>(process, statusEffectAddress + DataOffsets.MonsterStatusEffect.Id);
            float maxDuration = MemoryHelper.Read<float>(process, statusEffectAddress + DataOffsets.MonsterStatusEffect.MaxDuration);
            float currentBuildup = MemoryHelper.Read<float>(process, statusEffectAddress + DataOffsets.MonsterStatusEffect.CurrentBuildup);
            float maxBuildup = MemoryHelper.Read<float>(process, statusEffectAddress + DataOffsets.MonsterStatusEffect.MaxBuildup);
            float currentDuration = MemoryHelper.Read<float>(process, statusEffectAddress + DataOffsets.MonsterStatusEffect.CurrentDuration);
            int timesActivatedCount = MemoryHelper.Read<int>(process, statusEffectAddress + DataOffsets.MonsterStatusEffect.TimesActivatedCount);

            var statusEffect = monster.UpdateAndGetStatusEffect(statusEffectAddress, id, maxBuildup, currentBuildup, maxDuration, currentDuration, timesActivatedCount);
        }
    }
}
