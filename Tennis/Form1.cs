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

			// Year
			for (var i = 0; i < 2; i++)
				cbYear.Items.Add($"{i + 2025}");

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

			cbYear.SelectedIndex = 0;
			cbCourtNumber.SelectedIndex = 2;
			cbMonth.SelectedIndex = DateTime.Now.Month >= 12 ? 0 : DateTime.Now.Month ;
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
			Clipboard.SetText(data.Acc.ID);
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
	}
}
