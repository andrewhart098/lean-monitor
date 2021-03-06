using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Messaging;
using QuantConnect.Lean.Monitor.Model;
using QuantConnect.Lean.Monitor.Model.Messages;
using QuantConnect.Lean.Monitor.Model.Sessions;
using QuantConnect.Lean.Monitor.ViewModel.Charts;

namespace QuantConnect.Lean.Monitor.ViewModel
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly ISessionService _resultService;
        private readonly IMessenger _messenger;

        private ObservableCollection<DocumentViewModel> _charts = new ObservableCollection<DocumentViewModel>();

        private string _sessionName;

        public RelayCommand ExitCommand { get; private set; }
        public RelayCommand OpenSessionCommand { get; private set; }
        public RelayCommand CloseCommand { get; private set; }

        public ObservableCollection<DocumentViewModel> Charts
        {
            get { return _charts; }
            set
            {
                _charts = value;
                RaisePropertyChanged();
            }
        }

        public string SessionName
        {
            get { return _sessionName; }
            set
            {
                if (_sessionName == value) return;
                _sessionName = value;
                RaisePropertyChanged();
            }
        }

        public MainWindowViewModel(ISessionService resultService, IMessenger messenger)
        {
            _resultService = resultService;
            _messenger = messenger;

            if (IsInDesignMode)
            {
                SessionName = "localhost:1000";
            }

            ExitCommand = new RelayCommand(() => Application.Current.Shutdown());
            CloseCommand = new RelayCommand(() => _resultService.CloseSession());
            OpenSessionCommand = new RelayCommand(() => _messenger.Send(new ShowNewSessionWindowMessage()));

            _messenger.Register<SessionOpenedMessage>(this, message => SessionName = message.Name);

            _messenger.Register<SessionClosedMessage>(this, message =>
            {
                SessionName = string.Empty;
                Charts.Clear();
            });

            _messenger.Register<SessionUpdateMessage>(this, message =>
            {
                try
                {
                    lock (_charts)
                    {
                        ParseResult(message.Result);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });

            _messenger.Register<GridRequestMessage>(this, message =>
            {
                var chartTableViewModel = new GridPanelViewModel
                {
                    Key = message.Key,
                };

                // Calcualte the index for this tab
                var index = Charts.IndexOf(Charts.First(c => c.Key == message.Key));
                Charts.Insert(index, chartTableViewModel);

                chartTableViewModel.IsSelected = true;

                // Get the latest data for this tab and inject it
                var chart = _resultService.LastResult.Charts[message.Key];
                chartTableViewModel.ParseChart(chart);
            });
        }

        public void Initialize()
        {
            _resultService.Initialize();
        }

        private void ParseResult(Result messageResult)
        {
            foreach (var chart in messageResult.Charts
                .OrderBy(c => c.Key != "Strategy Equity")
                .ThenBy(c => c.Key != "Benchmark")
                .ThenBy(c => c.Key)
                .ToList())
            {
                if (chart.Value.Series.Count == 0)
                {
                    // This chart has no data.
                    // In example, a StockPlot:SYMBOL chart for which no subscription exists.
                }

                var chartDrawViewModel = Charts.OfType<ChartViewModelBase>().SingleOrDefault(c => c.Key == chart.Key);
                if (chartDrawViewModel == null)
                {
                    switch (chart.Value.Name)
                    {
                        case "Strategy Equity":
                            if (chart.Value?.Series?.Count > 2)
                            {
                                // Apparently the used has added series. this is not supported by the special tab.
                                // Therefore we use the default view in this case.
                                chartDrawViewModel = new ChartViewModel();
                            }
                            else
                            {
                                // Normal series. Use the special strategy equity tab
                                chartDrawViewModel = new StrategyEquityChartViewModel();
                            }
                            
                            break;

                        case "Benchmark":
                            if (chart.Value?.Series?.Count > 1)
                            {
                                // Apparently the used has added series. this is not supported by the special tab.
                                // Therefore we use the default view in this case.
                                chartDrawViewModel = new ChartViewModel();
                            }
                            else
                            {
                                // use the special benchmark tab
                                chartDrawViewModel = new BenchmarkChartViewModel();
                            }
                                
                            break;

                        default:
                            // This is a user added chart
                            chartDrawViewModel = new ChartViewModel();
                            break;
                    }

                    chartDrawViewModel.Key = chart.Key;
                    Charts.Add(chartDrawViewModel);
                }

                var chartTableViewModel = Charts.OfType<GridPanelViewModelBase>().SingleOrDefault(c => c.Key == chart.Key);

                // Some viewmodels need the full result.
                // Others just need a single chart
                try
                {
                    (chartDrawViewModel as IChartParser)?.ParseChart(chart.Value);
                    (chartDrawViewModel as IResultParser)?.ParseResult(messageResult);

                    (chartTableViewModel as IChartParser)?.ParseChart(chart.Value);
                }
                catch (Exception e)
                {
                    // This individual chart failed to update. Log and continue
                    Console.WriteLine(e);
                }
            }

            RaisePropertyChanged(() => Charts);
        }
    }
}