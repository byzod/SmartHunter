﻿using SmartHunter.Config;
using SmartHunter.Core.Config;
using SmartHunter.Game.Config;

namespace SmartHunter.Game.Helpers
{
    public static class ConfigHelper
    {
        static readonly string s_MainFileName = "Config.json";

        static ConfigContainer<MainConfig> s_Main;
        static ConfigContainer<LocalizationConfig> s_Localization;
        static ConfigContainer<MonsterDataConfig> s_MonsterData;
        static ConfigContainer<PlayerDataConfig> s_PlayerData;

        public static ConfigContainer<MainConfig> Main
        {
            get
            {
                if (s_Main == null)
                {
                    s_Main = new ConfigContainer<MainConfig>(s_MainFileName);
                    s_Main.Changed += Main_Loaded;
                }

                return s_Main;
            }
        }

        public static ConfigContainer<LocalizationConfig> Localization
        {
            get
            {
                if (s_Localization == null)
                {
                    s_Localization = new ConfigContainer<LocalizationConfig>(Main.Values.LocalizationFileName);
                }

                return s_Localization;
            }
        }

        public static ConfigContainer<MonsterDataConfig> MonsterData
        {
            get
            {
                if (s_MonsterData == null)
                {
                    s_MonsterData = new ConfigContainer<MonsterDataConfig>(Main.Values.MonsterDataFileName);
                }

                return s_MonsterData;
            }
        }

        public static ConfigContainer<PlayerDataConfig> PlayerData
        {
            get
            {
                if (s_PlayerData == null)
                {
                    s_PlayerData = new ConfigContainer<PlayerDataConfig>(Main.Values.PlayerDataFileName);
                }

                return s_PlayerData;
            }
        }

        // Ensures configs are loaded
        public static void EnsureConfigs()
        {
            var main = Main;
            var localization = Localization;
            var monsterData = MonsterData;
            var playerData = PlayerData;
        }

        static void Main_Loaded(object sender, System.EventArgs e)
        {
            Localization.TryChangeFileName(Main.Values.LocalizationFileName);
            MonsterData.TryChangeFileName(Main.Values.MonsterDataFileName);
            PlayerData.TryChangeFileName(Main.Values.PlayerDataFileName);
        }
    }
}