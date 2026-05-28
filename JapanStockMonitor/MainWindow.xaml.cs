using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace JapanStockMonitor
{
    public partial class MainWindow : Window
    {
        private double currentPrice = 0;
        private string currentStock = "";

        private DispatcherTimer timer;
        private Random random = new Random();

        private static readonly HttpClient httpClient = new HttpClient();

        private Dictionary<string, double> previousPrices =
            new Dictionary<string, double>();

        private Dictionary<string, double> currentPrices =
            new Dictionary<string, double>();

        public MainWindow()
        {
            InitializeComponent();

            currencyComboBox.SelectedIndex = 0;
            historyFilterComboBox.SelectedIndex = 0;

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMinutes(1);
            timer.Tick += Timer_Tick;

            CreateHistoryTable();
            LoadHistory();
            LoadChart();
            UpdateRanking();
            LoadFavorites();
            Button_Click(null, null);
            deleteFavoriteButton.IsEnabled = false;
        }

        // =========================
        // Button events
        // =========================
        

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            updateButton.IsEnabled = false;

            try
            {
                rateLabel.Content = "読み込み中...";

                string selectedStock = "トヨタ";

                if (currencyComboBox.SelectedItem is ComboBoxItem item)
                {
                    selectedStock = item.Content.ToString();
                }

                UpdateLogo(selectedStock);
                UpdateNews(selectedStock);

                double oldPrice = 0;

                if (currentPrices.ContainsKey(selectedStock))
                {
                    oldPrice = currentPrices[selectedStock];
                }

                string symbol = GetApiSymbol(selectedStock);

                bool apiFailed = false;

                double stockPrice = await GetStockPriceFromApi(symbol);

                if (stockPrice == 0)
                {
                    apiFailed = true;

                    stockPrice = GetStockPrice(selectedStock);
                }

                currentPrice = stockPrice;
                currentStock = selectedStock;

                previousPrices[selectedStock] = oldPrice;
                currentPrices[selectedStock] = stockPrice;

                SaveRateHistory(currentStock, currentPrice);

                LoadChart();
                UpdateRanking();
                UpdateChangeText(stockPrice, oldPrice);
                UpdateRateLabel(stockPrice, apiFailed);
            }
            finally
            {
                updateButton.IsEnabled = true;
            }
        }
        private async Task<double> GetStockPriceFromApi(string symbol)
        {
            string apiKey = "自分のAPIキー";

            string url =
                $"https://www.alphavantage.co/query?function=GLOBAL_QUOTE&symbol={symbol}&apikey={apiKey}";

            try
            {
                string json = await httpClient.GetStringAsync(url);

                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("Global Quote", out JsonElement quote))
                {
                    if (quote.TryGetProperty("05. price", out JsonElement priceElement))
                    {
                        string priceText = priceElement.GetString();

                        if (double.TryParse(priceText, out double price))
                        {
                            return price;
                        }
                    }
                }
            }
            catch
            {
                return 0;
            }

            return 0;
        }

        private string GetApiSymbol(string selectedStock)
        {
            if (selectedStock == "トヨタ")
            {
                return "7203.T";
            }
            else if (selectedStock == "ソニー")
            {
                return "6758.T";
            }
            else if (selectedStock == "任天堂")
            {
                return "7974.T";
            }

            return "";
        }
        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentPrice == 0)
            {
                MessageBox.Show("先に株価を更新してください。");
                return;
            }

            if (!double.TryParse(amountTextBox.Text, out double amount))
            {
                MessageBox.Show("株数は数字で入力してください。");
                return;
            }

            if (amount <= 0)
            {
                MessageBox.Show("株数は1以上で入力してください。");
                return;
            }

            if (amount % 1 != 0)
            {
                MessageBox.Show("株数は整数で入力してください。");
                return;
            }

            double result = amount * currentPrice;
            double basePrice = GetBasePrice(currentStock);
            double profit = (currentPrice - basePrice) * amount;

            resultLabel.Text =
                $"評価額 : {result:N0} 円\n" +
                $"損益 : {profit:+#,#;-#,#;0} 円";

            SaveHistory(currentStock, currentPrice, amount, result);
            LoadHistory();

            amountTextBox.Clear();
        }

        private void DeleteHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (historyGrid.SelectedItem == null)
            {
                MessageBox.Show("削除する履歴を選択してください。");
                return;
            }

            DataRowView row = (DataRowView)historyGrid.SelectedItem;
            int id = Convert.ToInt32(row["id"]);

            MessageBoxResult result =
                MessageBox.Show("選択した履歴を削除しますか？",
                                "確認",
                                MessageBoxButton.YesNo);

            if (result == MessageBoxResult.Yes)
            {
                using (SqliteConnection connection =
                       new SqliteConnection("Data Source=stock.db"))
                {
                    connection.Open();

                    string sql = "DELETE FROM stock_history WHERE id = @id";

                    SqliteCommand command = new SqliteCommand(sql, connection);
                    command.Parameters.AddWithValue("@id", id);
                    command.ExecuteNonQuery();
                }

                LoadHistory();
                MessageBox.Show("履歴を削除しました。");
            }
        }

        private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog();

            dialog.Filter = "CSV files (*.csv)|*.csv";
            dialog.FileName = "stock_history.csv";

            if (dialog.ShowDialog() == true)
            {
                using (StreamWriter writer =
                       new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true)))
                {
                    writer.WriteLine("銘柄,株価,株数,合計,日時");

                    using (SqliteConnection connection =
                           new SqliteConnection("Data Source=stock.db"))
                    {
                        connection.Open();

                        string sql = @"
SELECT stock_name, price, amount, result, created_at
FROM stock_history
ORDER BY id DESC;
";

                        SqliteCommand command =
                            new SqliteCommand(sql, connection);

                        SqliteDataReader reader =
                            command.ExecuteReader();

                        while (reader.Read())
                        {
                            writer.WriteLine(
                                $"{reader["stock_name"]}," +
                                $"{reader["price"]}," +
                                $"{reader["amount"]}," +
                                $"{reader["result"]}," +
                                $"{reader["created_at"]}");
                        }
                    }
                }

                MessageBox.Show("CSVファイルを保存しました。");
            }
        }

        // =========================
        // CheckBox / ComboBox events
        // =========================

        private void Timer_Tick(object sender, EventArgs e)
        {
            Button_Click(sender, null);
        }

        private void autoUpdateCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            timer.Start();
        }

        private void autoUpdateCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            timer.Stop();
        }

        private void currencyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                Button_Click(sender, null);
                LoadChart();
            }
        }

        private void historyFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
            {
                LoadHistory();
            }
        }

        // =========================
        // Dark mode
        // =========================

        private void darkModeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            mainGrid.Background = new SolidColorBrush(Color.FromRgb(18, 18, 18));
            mainCard.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));

            titleText.Foreground = Brushes.White;
            subTitleText.Foreground = Brushes.White;
            rateLabel.Foreground = Brushes.White;
            resultLabel.Foreground = Brushes.White;
            maxMinText.Foreground = Brushes.White;
            chartTitleText.Foreground = Brushes.White;
            historyTitleText.Foreground = Brushes.White;
            darkModeCheckBox.Foreground = Brushes.White;
            autoUpdateCheckBox.Foreground = Brushes.White;
            amountTitleText.Foreground = Brushes.White;
            newsTitleText.Foreground = Brushes.White;
            newsText.Foreground = Brushes.White;
            rankingText.Foreground = Brushes.White;

            amountTextBox.Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
            amountTextBox.Foreground = Brushes.White;
            amountTextBox.BorderBrush = Brushes.Gray;

            historyGrid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty,
                        new SolidColorBrush(Color.FromRgb(55, 55, 55))),

                    new Setter(Control.ForegroundProperty,
                        Brushes.White),

                    new Setter(Control.BorderBrushProperty,
                        Brushes.Gray)
                }
            };
            favoriteTitleText.Foreground = Brushes.White;

            favoriteListBox.Background =
                new SolidColorBrush(Color.FromRgb(45, 45, 45));

            favoriteListBox.Foreground = Brushes.White;
        }

        private void darkModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            mainGrid.Background = new SolidColorBrush(Color.FromRgb(238, 242, 247));
            mainCard.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));

            titleText.Foreground = Brushes.Black;
            subTitleText.Foreground = Brushes.Gray;
            rateLabel.Foreground = Brushes.Black;
            resultLabel.Foreground = Brushes.Black;
            maxMinText.Foreground = Brushes.Gray;
            chartTitleText.Foreground = Brushes.Black;
            historyTitleText.Foreground = Brushes.Black;
            darkModeCheckBox.Foreground = Brushes.Black;
            autoUpdateCheckBox.Foreground = Brushes.Black;
            amountTitleText.Foreground = Brushes.Gray;
            newsTitleText.Foreground = Brushes.Black;
            newsText.Foreground = Brushes.Black;
            rankingText.Foreground = Brushes.Black;

            amountTextBox.Background = Brushes.White;
            amountTextBox.Foreground = Brushes.Black;
            amountTextBox.BorderBrush = Brushes.Gray;

            historyGrid.Background = Brushes.White;
            historyGrid.Foreground = Brushes.Black;
            historyGrid.ColumnHeaderStyle = null;

            favoriteTitleText.Foreground = Brushes.Black;

            favoriteListBox.Background = Brushes.White;
            favoriteListBox.Foreground = Brushes.Black;
        }

        // =========================
        // UI update methods
        // =========================

        private void UpdateLogo(string selectedStock)
        {
            if (selectedStock == "トヨタ")
            {
                flagImage.Source =
                    new BitmapImage(new Uri("Resources/toyota.png", UriKind.Relative));
            }
            else if (selectedStock == "ソニー")
            {
                flagImage.Source =
                    new BitmapImage(new Uri("Resources/sony.png", UriKind.Relative));
            }
            else if (selectedStock == "任天堂")
            {
                flagImage.Source =
                    new BitmapImage(new Uri("Resources/nintendo.png", UriKind.Relative));
            }
        }

        private void UpdateNews(string selectedStock)
        {
            if (selectedStock == "トヨタ")
            {
                newsText.Text =
                    "・トヨタ、EV市場拡大へ\n" +
                    "・新型プリウス販売好調";
            }
            else if (selectedStock == "ソニー")
            {
                newsText.Text =
                    "・ソニー、ゲーム事業好調\n" +
                    "・映画部門の売上増加";
            }
            else if (selectedStock == "任天堂")
            {
                newsText.Text =
                    "・任天堂、新作タイトル発表\n" +
                    "・Switch販売継続好調";
            }
        }

        private double GetStockPrice(string selectedStock)
        {
            if (selectedStock == "トヨタ")
            {
                return 2850 + random.Next(-20, 21);
            }
            else if (selectedStock == "ソニー")
            {
                return 3780 + random.Next(-30, 31);
            }
            else if (selectedStock == "任天堂")
            {
                return 11200 + random.Next(-50, 51);
            }

            return 0;
        }

        private double GetBasePrice(string selectedStock)
        {
            if (selectedStock == "トヨタ")
            {
                return 2800;
            }
            else if (selectedStock == "ソニー")
            {
                return 3700;
            }
            else if (selectedStock == "任天堂")
            {
                return 11000;
            }

            return 0;
        }

        private void UpdateChangeText(double stockPrice, double oldPrice)
        {
            if (oldPrice == 0)
            {
                previousPriceText.Text = "前回価格 : -";
                changeText.Text = "";
                trendText.Text = "";
                return;
            }

            previousPriceText.Text =
                $"前回価格 : {oldPrice:N0} 円";

            double difference = stockPrice - oldPrice;
            double percent = difference / oldPrice * 100;

            if (difference > 0)
            {
                changeText.Text = $"+{difference:N0} 円 (+{percent:F2}%) ↑";
                changeText.Foreground = Brushes.Green;

                trendText.Foreground = Brushes.LimeGreen;
            }
            else if (difference < 0)
            {
                changeText.Text = $"{difference:N0} 円 ({percent:F2}%) ↓";
                changeText.Foreground = Brushes.Red;

                trendText.Foreground = Brushes.OrangeRed;
            }
            else
            {
                changeText.Text = "変化なし";
                changeText.Foreground = Brushes.Gray;

                trendText.Foreground = Brushes.Gray;
            }
        }

        private void UpdateRateLabel(double stockPrice, bool apiFailed)
        {
            string apiText = apiFailed ? "参考データ" : "API取得成功";

            rateLabel.Content =
                $"{stockPrice:N0} 円\n" +
                $"{apiText}\n" +
                $"更新時間 : {DateTime.Now:HH:mm:ss}";

            if (darkModeCheckBox.IsChecked == true)
            {
                rateLabel.Foreground = Brushes.White;
            }
            else
            {
                rateLabel.Foreground = Brushes.Black;
            }
        }

        // =========================
        // Database methods
        // =========================

        private void CreateHistoryTable()
        {
            using (SqliteConnection connection =
                   new SqliteConnection("Data Source=stock.db"))
            {
                connection.Open();

                string stockSql = @"
CREATE TABLE IF NOT EXISTS stock_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    stock_name TEXT NOT NULL,
    price REAL NOT NULL,
    amount REAL NOT NULL,
    result REAL NOT NULL,
    created_at TEXT NOT NULL
);
";

                string rateSql = @"
CREATE TABLE IF NOT EXISTS rate_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    stock_name TEXT NOT NULL,
    price REAL NOT NULL,
    created_at TEXT NOT NULL
);
";
                string favoriteSql = @"
CREATE TABLE IF NOT EXISTS favorites (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    stock_name TEXT NOT NULL UNIQUE,
    created_at TEXT NOT NULL
);
";
                SqliteCommand stockCommand = new SqliteCommand(stockSql, connection);
                stockCommand.ExecuteNonQuery();

                SqliteCommand rateCommand = new SqliteCommand(rateSql, connection);
                rateCommand.ExecuteNonQuery();
                SqliteCommand favoriteCommand = new SqliteCommand(favoriteSql, connection);
                favoriteCommand.ExecuteNonQuery();
            }
        }

        private void SaveHistory(string stockName, double price, double amount, double result)
        {
            using (SqliteConnection connection =
                   new SqliteConnection("Data Source=stock.db"))
            {
                connection.Open();

                string sql = @"
INSERT INTO stock_history
(stock_name, price, amount, result, created_at)
VALUES
(@stock_name, @price, @amount, @result, @created_at);
";

                SqliteCommand command = new SqliteCommand(sql, connection);

                command.Parameters.AddWithValue("@stock_name", stockName);
                command.Parameters.AddWithValue("@price", price);
                command.Parameters.AddWithValue("@amount", amount);
                command.Parameters.AddWithValue("@result", result);
                command.Parameters.AddWithValue("@created_at",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                command.ExecuteNonQuery();
            }
        }

        private void SaveRateHistory(string stockName, double price)
        {
            using (SqliteConnection connection =
                   new SqliteConnection("Data Source=stock.db"))
            {
                connection.Open();

                string sql = @"
INSERT INTO rate_history
(stock_name, price, created_at)
VALUES
(@stock_name, @price, @created_at);
";

                SqliteCommand command = new SqliteCommand(sql, connection);

                command.Parameters.AddWithValue("@stock_name", stockName);
                command.Parameters.AddWithValue("@price", price);
                command.Parameters.AddWithValue("@created_at",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                command.ExecuteNonQuery();
            }
        }

        private void LoadHistory()
        {
            using (SqliteConnection connection =
                   new SqliteConnection("Data Source=stock.db"))
            {
                connection.Open();

                string selectedFilter = "ALL";

                if (historyFilterComboBox.SelectedItem is ComboBoxItem item)
                {
                    selectedFilter = item.Content.ToString();
                }

                string sql;

                if (selectedFilter == "ALL")
                {
                    sql = @"
SELECT id, stock_name, price, amount, result, created_at
FROM stock_history
ORDER BY id DESC;
";
                }
                else
                {
                    sql = @"
SELECT id, stock_name, price, amount, result, created_at
FROM stock_history
WHERE stock_name = @stock_name
ORDER BY id DESC;
";
                }

                SqliteCommand command = new SqliteCommand(sql, connection);

                if (selectedFilter != "ALL")
                {
                    command.Parameters.AddWithValue("@stock_name", selectedFilter);
                }

                SqliteDataReader reader = command.ExecuteReader();

                DataTable table = new DataTable();
                table.Load(reader);

                historyGrid.ItemsSource = table.DefaultView;
            }
        }

        // =========================
        // Chart / ranking methods
        // =========================

        private void LoadChart()
        {
            using (SqliteConnection connection =
                   new SqliteConnection("Data Source=stock.db"))
            {
                connection.Open();

                string selectedStock = currentStock;

                if (string.IsNullOrEmpty(selectedStock))
                {
                    selectedStock = "トヨタ";
                }

                chartTitleText.Text = $"{selectedStock} 株価推移";

                string sql = @"
SELECT price, created_at
FROM (
    SELECT price, created_at
    FROM rate_history
    WHERE stock_name = @stock_name
    ORDER BY id DESC
    LIMIT 8
)
ORDER BY created_at ASC;
";

                SqliteCommand command = new SqliteCommand(sql, connection);
                command.Parameters.AddWithValue("@stock_name", selectedStock);

                SqliteDataReader reader = command.ExecuteReader();

                List<double> prices = new List<double>();
                List<string> times = new List<string>();

                while (reader.Read())
                {
                    prices.Add(reader.GetDouble(0));

                    DateTime time = DateTime.Parse(reader.GetString(1));
                    times.Add(time.ToString("HH:mm"));
                }

                rateChart.Series = new ISeries[]
                {
                    new LineSeries<double>
                    {
                        Values = prices,
                        Fill = null,
                        GeometrySize = 10,
                        LineSmoothness = 0.7
                    }
                };

                rateChart.XAxes = new[]
                {
                    new Axis
                    {
                        Labels = times
                    }
                };

                rateChart.YAxes = new[]
                {
                    new Axis
                    {
                        Labeler = value => value.ToString("N0")
                    }
                };

                if (prices.Count > 0)
                {
                    double max = prices.Max();
                    double min = prices.Min();

                    maxMinText.Text =
                        $"最高値 : {max:N0} 円   最安値 : {min:N0} 円";
                }
                else
                {
                    maxMinText.Text = "グラフデータがありません";
                }
            }
        }

        private void UpdateRanking()
        {
            var ranking = currentPrices
                .Where(x =>
                    previousPrices.ContainsKey(x.Key) &&
                    previousPrices[x.Key] != 0)
                .Select(x => new
                {
                    Name = x.Key,
                    Percent =
                        (x.Value - previousPrices[x.Key])
                        / previousPrices[x.Key] * 100
                })
                .OrderByDescending(x => x.Percent)
                .ToList();

            if (ranking.Count == 0)
            {
                rankingText.Text =
                    "📈 値上がりランキング";
                return;
            }

            rankingText.Text =
                "📈 値上がりランキング\n" +
                string.Join("\n",
                    ranking.Select((x, index) =>
                        $"{index + 1}位 " +
                        $"{x.Name} " +
                        $"{x.Percent:+0.00;-0.00}%"));
        }

        private void AddFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentStock))
            {
                MessageBox.Show("先に銘柄を更新してください。");
                return;
            }

            using (SqliteConnection connection =
                   new SqliteConnection("Data Source=stock.db"))
            {
                connection.Open();

                string sql = @"
INSERT OR IGNORE INTO favorites
(stock_name, created_at)
VALUES
(@stock_name, @created_at);
";

                SqliteCommand command = new SqliteCommand(sql, connection);

                command.Parameters.AddWithValue("@stock_name", currentStock);
                command.Parameters.AddWithValue("@created_at",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                int result = command.ExecuteNonQuery();

                if (result == 0)
                {
                    MessageBox.Show("すでにお気に入りに登録されています。");
                }
                else
                {
                    MessageBox.Show("お気に入りに追加しました。");
                }
                LoadFavorites();
                favoriteListBox.SelectedItem = null;
            }
        }

        private void UpdateFavoriteCount()
        {
            int count = favoriteListBox.Items.Count;

            favoriteTitleText.Text =
                $"⭐ お気に入り銘柄 ({count})";
        }

        private void LoadFavorites()
        {
            using (SqliteConnection connection =
                   new SqliteConnection("Data Source=stock.db"))
            {
                connection.Open();

                string sql = @"
SELECT stock_name
FROM favorites
ORDER BY id DESC;
";

                SqliteCommand command = new SqliteCommand(sql, connection);
                SqliteDataReader reader = command.ExecuteReader();

                favoriteListBox.Items.Clear();

                while (reader.Read())
                {
                    favoriteListBox.Items.Add(reader["stock_name"].ToString());
                }

                UpdateFavoriteCount();
            }
        }

        private void favoriteListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (favoriteListBox.SelectedItem == null)
            {
                deleteFavoriteButton.IsEnabled = false;
                return;
            }

            deleteFavoriteButton.IsEnabled = true;

            string selectedFavorite = favoriteListBox.SelectedItem.ToString();

            foreach (ComboBoxItem item in currencyComboBox.Items)
            {
                if (item.Content.ToString() == selectedFavorite)
                {
                    currencyComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void DeleteFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (favoriteListBox.SelectedItem == null)
            {
                MessageBox.Show("削除する銘柄を選択してください。");
                return;
            }

            string selectedFavorite =
                favoriteListBox.SelectedItem.ToString();

            using (SqliteConnection connection =
                   new SqliteConnection("Data Source=stock.db"))
            {
                connection.Open();

                string sql =
                    "DELETE FROM favorites WHERE stock_name = @stock_name";

                SqliteCommand command =
                    new SqliteCommand(sql, connection);

                command.Parameters.AddWithValue("@stock_name", selectedFavorite);

                command.ExecuteNonQuery();
            }

            LoadFavorites();

            favoriteListBox.SelectedItem = null;

            deleteFavoriteButton.IsEnabled = false;

            MessageBox.Show("お気に入りを削除しました。");
        }
    }
}
