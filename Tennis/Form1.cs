using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tennis
{
	public partial class Form1 : Form
	{
		private Dictionary<string, Account> account = new();
		private List<ReservationThread> threads = new List<ReservationThread>();

		public Form1()
		{
			InitializeComponent();
			InitData();
		}

		private void InitData()
		{
			// Json Account
			string path = Application.StartupPath + @"\config.json";
			using (StreamReader file = File.OpenText(path))
			{
				using (JsonTextReader reader = new JsonTextReader(file))
				{
					JObject json = (JObject)JToken.ReadFrom(reader);
					foreach (var v in json)
					{
						var acc = new Account()
						{
							ID = v.Key,
							PW = v.Value["PW"].ToString(),
							SPW = v.Value["SPW"].ToString()
						};
						account.Add(v.Key, acc);
					}
				}
			}

			// Id
			foreach (var v in account.Keys)
				cbId.Items.Add(v);
			cbId.SelectedIndex = 0;

			// Court
			cbCourt.Items.Add("양재");
			cbCourt.Items.Add("내곡");
			cbCourt.SelectedIndex = 0;

			// Court Number
			for (var i = 0; i < 8; i++)
				cbCourtNumber.Items.Add($"{i + 1}번");

			cbCourtNumber.Items.Add('A');
			cbCourtNumber.Items.Add('B');
			cbCourtNumber.Items.Add('C');
			cbCourt.SelectedIndex = 0;

			var year = DateTime.Now.Year;
			// Year
			for (var i = 0; i < 2; i++)
				cbYear.Items.Add($"{i + year}");

			// Month
			for (var i = 0; i < 12; i++)
				cbMonth.Items.Add($"{i + 1}");

			// day
			for (var i = 0; i < 31; i++)
				cbDay.Items.Add($"{i + 1}");

			// start, end
			for (var i = 0; i < 24; i++)
			{
				cbStartTime.Items.Add($"{i + 1}");
				cbEndTime.Items.Add($"{i + 1}");
			}

			cbWeek.Items.Add("일요일");
			cbWeek.Items.Add("월요일");
			cbWeek.Items.Add("화요일");
			cbWeek.Items.Add("수요일");
			cbWeek.Items.Add("목요일");
			cbWeek.Items.Add("금요일");
			cbWeek.Items.Add("토요일");

			cbWeek.SelectedIndex = 6;
			cbYear.SelectedIndex = 0;
			cbCourtNumber.SelectedIndex = 2;
			cbMonth.SelectedIndex = DateTime.Now.Month >= 12 ? 0 : DateTime.Now.Month;
			cbDay.SelectedIndex = 0;
			cbStartTime.SelectedIndex = 10 - 1;
			cbEndTime.SelectedIndex = 13 - 1;

			reservationDelay.Checked = true;
		}

		private ReservationData GetData()
		{
			var data = new ReservationData();
			string acc = cbId.SelectedItem.ToString();
			data.Acc = account[acc];
			data.Court = cbCourt.SelectedItem.ToString();
			data.CourtNumber = cbCourtNumber.SelectedItem.ToString();
			data.Year = int.Parse(cbYear.SelectedItem.ToString());
			data.Month = int.Parse(cbMonth.SelectedItem.ToString());
			data.Day = int.Parse(cbDay.SelectedItem.ToString());
			data.StartTime = int.Parse(cbStartTime.SelectedItem.ToString());
			data.EndTime = int.Parse(cbEndTime.SelectedItem.ToString());
			data.IsDelay = reservationDelay.Checked;

			return data;
		}

		private void OnClickStart(object sender, EventArgs e)
		{
			Thread trd = new(StartReservation);
			trd.IsBackground = true;
			var data = GetData();
			trd.SetApartmentState(ApartmentState.STA);
			ReservationThread tp = new ReservationThread(data);
			trd.Start(tp);
			threads.Add(tp);
		}

		private void OnClickStop(object sender, EventArgs e)
		{
			foreach (var v in threads)
				v.OnClose();

			threads.Clear();
		}

		private static void StartReservation(object obj)
		{
			var v = obj as ReservationThread;
			v.DoStart();
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			OnClickStop(null, null);
		}

		private void weekend_Click(object sender, EventArgs e)
		{
			int year = int.Parse(cbYear.SelectedItem.ToString());
			int month = int.Parse(cbMonth.SelectedItem.ToString());
			DayOfWeek week = (DayOfWeek)cbWeek.SelectedIndex;
			FindDayOfWeekList(year, month, week, out var list);

			int i = 0;
			foreach (var v in list)
			{
				Thread trd = new(StartReservation);
				trd.IsBackground = true;
				var data = GetData();
				data.Day = v.Day;
				data.StartDelay = 5000 * i;
				trd.SetApartmentState(ApartmentState.STA);
				ReservationThread tp = new ReservationThread(data);
				trd.Start(tp);
				threads.Add(tp);
				i++;
			}
		}

		private void FindDayOfWeekList(int year, int month, DayOfWeek dayOfWeek, out List<DateTime> outData)
		{
			// 현재 연도와 월 가져오기
			DateTime today = DateTime.Today;
			

			// 해당 월의 첫 날과 마지막 날 계산
			DateTime firstDay = new DateTime(year, month, 1);
			DateTime lastDay = firstDay.AddMonths(1).AddDays(-1);

			// 토요일 리스트 저장
			outData = new List<DateTime>();

			// 첫 날부터 마지막 날까지 루프
			for (DateTime date = firstDay; date <= lastDay; date = date.AddDays(1))
			{
				if (date.DayOfWeek == dayOfWeek)
				{
					outData.Add(date);
				}
			}
		}
	}
}
