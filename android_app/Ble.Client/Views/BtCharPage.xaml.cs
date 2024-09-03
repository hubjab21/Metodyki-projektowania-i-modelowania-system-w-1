using Plugin.BLE.Abstractions.Contracts;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using XamarinEssentials = Xamarin.Essentials;
using System.Timers;
using Plugin.BLE.Abstractions.EventArgs;
using System.Globalization;

namespace Ble.Client
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class BtCharPage : ContentPage
    {
        private readonly IDevice _connectedDevice;
        private readonly IService _selectedService;
        private readonly List<ICharacteristic> _charList = new List<ICharacteristic>();
        private ICharacteristic _char;
        private Timer _pollingTimer;
        private float maxSpeed = 0;
        private float _totalDistance = 0;
        private float totalSpeed = 0;
        private float speedCount = 0;
        private float minSpeed = float.MaxValue;
        public BtCharPage(IDevice connectedDevice, IService selectedService)
        {
            InitializeComponent();

            _connectedDevice = connectedDevice;
            _selectedService = selectedService;
            _char = null;

            bleDevice.Text = "Selected BLE device: " + _connectedDevice.Name;
            bleService.Text = "Selected BLE service: " + _selectedService.Name;
        }

        protected async override void OnAppearing()
        {
            base.OnAppearing();
            try
            {
                if (_selectedService != null)
                {
                    var charListReadOnly = await _selectedService.GetCharacteristicsAsync();

                    _charList.Clear();
                    var charListStr = new List<string>();
                    for (int i = 0; i < charListReadOnly.Count; i++)
                    {
                        _charList.Add(charListReadOnly[i]);
                        charListStr.Add(i.ToString() + ": " + charListReadOnly[i].Name);
                    }
                    foundBleChars.ItemsSource = charListStr;
                }
                else
                {
                    ErrorLabel.Text = GetTimeNow() + "UART GATT service not found." + Environment.NewLine;
                }
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = GetTimeNow() + ": Error initializing UART GATT service. Exception: " + ex.Message;
            }
        }

        private async void FoundBleChars_ItemTapped(object sender, ItemTappedEventArgs e)
        {
            _char = _charList[e.ItemIndex];
            if (_char != null)
            {
                bleChar.Text = _char.Name + "\n" +
                    "UUID: " + _char.Uuid.ToString() + "\n" +
                    "Read: " + _char.CanRead + "\n" +
                    "Write: " + _char.CanWrite + "\n" +
                    "Update: " + _char.CanUpdate;

                var charDescriptors = await _char.GetDescriptorsAsync();
                bleChar.Text += "\nDescriptors (" + charDescriptors.Count + "): ";
                for (int i = 0; i < charDescriptors.Count; i++)
                    bleChar.Text += charDescriptors[i].Name + ", ";

                // Unsubscribe previous characteristic if exists
                if (_char.CanUpdate)
                {
                    await _char.StopUpdatesAsync();
                    _char.ValueUpdated -= OnCharacteristicValueUpdated;
                }

                // Subscribe to the ValueUpdated event for the selected characteristic
                if (_char.CanUpdate)
                {
                    _char.ValueUpdated += OnCharacteristicValueUpdated;
                    await _char.StartUpdatesAsync();
                }

                // Start or stop the polling timer based on characteristic capability
                if (_char.CanRead && !_char.CanUpdate)
                {
                    StartPollingTimer();
                }
                else
                {
                    StopPollingTimer();
                }
            }
        }

        private void OnCharacteristicValueUpdated(object sender, CharacteristicUpdatedEventArgs args)
        {
            var receivedBytes = args.Characteristic.Value;
            UpdateOutput(receivedBytes);
        }

        private void UpdateOutput(byte[] receivedBytes)
        {
            var receivedString = $"Speed: ";
            var receivedString2 = Encoding.UTF8.GetString(receivedBytes, 0, receivedBytes.Length);
            receivedString += receivedString2;
            receivedString += $" [Km/h] " + Environment.NewLine;
            receivedString += $"Max Speed: ";

            float nowSpeed = float.Parse(receivedString2, CultureInfo.InvariantCulture);
            totalSpeed += nowSpeed;
            speedCount++;
            if (nowSpeed > maxSpeed)
            {
                maxSpeed = nowSpeed;
            }
            string maxSpeedString = maxSpeed.ToString();
            receivedString += maxSpeedString;
            receivedString += $" [Km/h] ";
            float distanceIncrement = (1f / 7200f) * nowSpeed;
            _totalDistance += distanceIncrement;
            string totalDistanceString = _totalDistance.ToString();
            receivedString += $"Distance Traveled: ";
            receivedString += totalDistanceString;
            receivedString += $" [Km] ";
            if (minSpeed == float.MaxValue || (nowSpeed > 0 && nowSpeed < minSpeed))
            {
                minSpeed = nowSpeed;
            }
            string minSpeedString = minSpeed.ToString();
            receivedString += $"Min Speed: ";
            receivedString += minSpeedString;
            receivedString += $" [Km/h] ";

            float averageSpeed = totalSpeed / speedCount;
            string averageSpeedString = averageSpeed.ToString();
            receivedString += $"Average Speed: ";
            receivedString += averageSpeedString;
            receivedString += $" [Km/h] " + Environment.NewLine;

            XamarinEssentials.MainThread.BeginInvokeOnMainThread(() =>
            {
                Output.Text = receivedString + Environment.NewLine;
            });
        }

        private void StartPollingTimer()
        {
            if (_pollingTimer == null)
            {
                _pollingTimer = new Timer(500); // Set timer to 0.5 seconds
                _pollingTimer.Elapsed += async (sender, e) => await PollCharacteristic();
                _pollingTimer.AutoReset = true;
            }
            _pollingTimer.Start();
        }

        private void StopPollingTimer()
        {
            _pollingTimer?.Stop();
        }

        private async Task PollCharacteristic()
        {
            try
            {
                if (_char != null && _char.CanRead)
                {
                    var receivedBytes = await _char.ReadAsync();
                    UpdateOutput(receivedBytes);
                }
            }
            catch (Exception ex)
            {
                XamarinEssentials.MainThread.BeginInvokeOnMainThread(() =>
                {
                    ErrorLabel.Text = GetTimeNow() + ": Error polling characteristic. Exception: " + ex.Message;
                });
            }
        }

        private async void RegisterCommandButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                if (_char != null)
                {
                    if (_char.CanUpdate)
                    {
                        _char.ValueUpdated += OnCharacteristicValueUpdated;
                        await _char.StartUpdatesAsync();
                        ErrorLabel.Text = GetTimeNow() + ": Notify callback function registered successfully.";
                    }
                    else
                    {
                        ErrorLabel.Text = GetTimeNow() + ": Characteristic does not have a notify function.";
                    }
                }
                else
                {
                    ErrorLabel.Text = GetTimeNow() + ": No characteristic selected.";
                }
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = GetTimeNow() + ": Error initializing UART GATT service. Exception: " + ex.Message;
            }
        }

        private async void ReceiveCommandButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                if (_char != null)
                {
                    if (_char.CanRead)
                    {
                        var receivedBytes = await _char.ReadAsync();
                        UpdateOutput(receivedBytes);
                    }
                    else
                    {
                        ErrorLabel.Text = GetTimeNow() + ": Characteristic does not support read.";
                    }
                }
                else
                    ErrorLabel.Text = GetTimeNow() + ": No Characteristic selected.";
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = GetTimeNow() + ": Error receiving Characteristic. Exception: " + ex.Message;
            }
        }

        private async void SendCommandButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                if (_char != null)
                {
                    if (_char.CanWrite)
                    {
                        byte[] array = Encoding.UTF8.GetBytes(CommandTxt.Text);
                        await _char.WriteAsync(array);
                    }
                    else
                    {
                        ErrorLabel.Text = GetTimeNow() + ": Characteristic does not support Write";
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = GetTimeNow() + ": Error receiving Characteristic. Exception: " + ex.Message;
            }
        }

        private async void StartButton_Clicked(object sender, EventArgs e)
        {
            await SendCommand("START");
        }

        private async void StopButton_Clicked(object sender, EventArgs e)
        {
            await SendCommand("STOP");
        }

        private async Task SendCommand(string command)
        {
            try
            {
                if (_char != null)
                {
                    if (_char.CanWrite)
                    {
                        byte[] array = Encoding.UTF8.GetBytes(command);
                        await _char.WriteAsync(array);
                        Output.Text += $"{command} command sent successfully.{Environment.NewLine}";
                    }
                    else
                    {
                        ErrorLabel.Text = GetTimeNow() + ": Characteristic does not support Write";
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = GetTimeNow() + ": Error sending command. Exception: " + ex.Message;
            }
        }

        private string GetTimeNow()
        {
            var timestamp = DateTime.Now;
            return timestamp.Hour.ToString() + ":" + timestamp.Minute.ToString() + ":" + timestamp.Second.ToString();
        }
    }
}
