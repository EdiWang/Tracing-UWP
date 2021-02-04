using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Microsoft.Toolkit.Uwp.Helpers;
using Tracing.Configuration;
using Tracing.Helpers;
using Tracing.Models;

namespace Tracing.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        public AppSettings Settings { get; set; }

        private ObservableCollection<Language> _languages;
        private Language _selectedLanguage;
        private bool _enableLanguageSelection;

        public RelayCommand CommandPenSettings { get; set; }

        public RelayCommand CommandWheelSettings { get; set; }

        public RelayCommand CommandResetDefaultCanvasSize { get; set; }

        public string Publisher => Edi.UWP.Helpers.Utils.GetAppPublisher();

        public string Version => $"{SystemInformation.ApplicationVersion.Major}.{SystemInformation.ApplicationVersion.Minor}.{SystemInformation.ApplicationVersion.Build}.{SystemInformation.ApplicationVersion.Revision}";

        public string OperatingSystemVersion => $"OS Build: {SystemInformation.OperatingSystemVersion}";

        public string CPUArch => SystemInformation.OperatingSystemArchitecture.ToString();

        public bool EnableLanguageSelection
        {
            get => _enableLanguageSelection;
            set { _enableLanguageSelection = value; RaisePropertyChanged(); }
        }

        public ObservableCollection<Language> Languages
        {
            get => _languages;
            set { _languages = value; RaisePropertyChanged(); }
        }

        public Language SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                _selectedLanguage = value;
                Settings.PrimaryLanguageOverride = value.LanguageCode;
                RaisePropertyChanged();
            }
        }

        private string _canvasPixelWidth;
        private string _canvasPixelHeight;

        public string CanvasPixelWidth
        {
            get => _canvasPixelWidth;
            set
            {
                _canvasPixelWidth = value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (Regex.IsMatch(value, "^[1-9][0-9]*$"))
                    {
                        Settings.DefaultCanvasWidth = int.Parse(value);
                    }
                }
                RaisePropertyChanged();
            }
        }

        public string CanvasPixelHeight
        {
            get => _canvasPixelHeight;
            set
            {
                _canvasPixelHeight = value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (Regex.IsMatch(value, "^[1-9][0-9]*$"))
                    {
                        Settings.DefaultCanvasHeight = int.Parse(value);
                    }
                }
                RaisePropertyChanged();
            }
        }

        public SettingsViewModel()
        {
            Settings = App.AppSettings;

            CanvasPixelHeight = Settings.DefaultCanvasHeight.ToString();
            CanvasPixelWidth = Settings.DefaultCanvasWidth.ToString();

            CommandPenSettings = new RelayCommand(async () =>
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:pen"));
            });

            CommandWheelSettings = new RelayCommand(async () =>
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:wheel"));
            });

            CommandResetDefaultCanvasSize = new RelayCommand(() =>
            {
                var size = Helper.GetCanvasSizeFromScreenResolution();
                if (size.Height > 0 && size.Width > 0)
                {
                    Settings.DefaultCanvasHeight = (int)size.Height;
                    Settings.DefaultCanvasWidth = (int)size.Width;

                    CanvasPixelHeight = Settings.DefaultCanvasHeight.ToString();
                    CanvasPixelWidth = Settings.DefaultCanvasWidth.ToString();
                }
            });

            EnableLanguageSelection = Settings.UsePrimaryLanguageOverride;
            Languages = new ObservableCollection<Language>
            {
                new Language { DisplayName = "English (United States)", LanguageCode = "en-US" },
                new Language { DisplayName = "简体中文 (中国)", LanguageCode = "zh-CN" }
            };

            var selectedLanguage = Settings.PrimaryLanguageOverride;
            if (!string.IsNullOrEmpty(selectedLanguage))
            {
                var f = Languages.FirstOrDefault(l => l.LanguageCode == selectedLanguage);
                if (null != f)
                {
                    SelectedLanguage = f;
                }
            }
            else
            {
                SelectedLanguage = Languages.FirstOrDefault();
            }
        }
    }
}
