using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using System.Data.OleDb;
using System.Configuration;
using System.Threading;
using System.Web;
using System.Net;
using System.IO;

namespace BulkSMSPostingApplication2
{
    static public class QueryInProgram
    {
        public static String UserPopulate = "SELECT USERINFO.USERID, " +
               "USERINFO.Badgenumber, " +
               "USERINFO.Name, " +
               "NUM_RUN_DEIL.SCHCLASSID, " +
               "USERINFO.PAGER " +
               "FROM " +
               "(USERINFO INNER JOIN USER_OF_RUN " +
                                        "ON USERINFO.USERID = USER_OF_RUN.USERID) " +
               "INNER JOIN NUM_RUN_DEIL " +
                                        "ON USER_OF_RUN.NUM_OF_RUN_ID = NUM_RUN_DEIL.NUM_RUNID " +
               "where IIf(Now()>=USER_OF_RUN.STARTDATE and Now()<=USER_OF_RUN.ENDDATE , 1, 0)=1 " +
               "and NUM_RUN_DEIL.SDAYS=weekday(now());";

        public static String UserCount = "SELECT " +
                "count(*) as CountData " +
                "FROM " +
                "(USERINFO INNER JOIN USER_OF_RUN " +
                                         "ON USERINFO.USERID = USER_OF_RUN.USERID) " +
                "INNER JOIN NUM_RUN_DEIL " +
                                         "ON USER_OF_RUN.NUM_OF_RUN_ID = NUM_RUN_DEIL.NUM_RUNID " +
                "where IIf(Now()>=USER_OF_RUN.STARTDATE and Now()<=USER_OF_RUN.ENDDATE , 1, 0)=1 " +
                "and NUM_RUN_DEIL.SDAYS=weekday(now());";


        public static String ShiftPopulate = "SELECT SchClass.schClassid, SchClass.schName, SchClass.StartTime, SchClass.EndTime, " +
                "SchClass.LateMinutes, SchClass.EarlyMinutes, SchClass.CheckInTime1, SchClass.CheckInTime2, SchClass.CheckOutTime1, " +
                "SchClass.CheckOutTime2 FROM SchClass;";

        public static String ConnectionSMSDB = ConfigurationManager.AppSettings["SMSDBPath"].ToString();
        public static String ConnectionMainDB = ConfigurationManager.AppSettings["DBFilePath"].ToString();

        public static int EnableMsgRegular = Int32.Parse(ConfigurationManager.AppSettings["EnableMsgRegular"].ToString());
        public static int EnableMsgLate = Int32.Parse(ConfigurationManager.AppSettings["EnableMsgLate"].ToString());
        public static int EnableMsgAbsent = Int32.Parse(ConfigurationManager.AppSettings["EnableMsgAbsent"].ToString());
        public static int EnableMsgEarlyLeave = Int32.Parse(ConfigurationManager.AppSettings["EnableMsgEarlyLeave"].ToString());
        public static int EnableMsgRegularLeave = Int32.Parse(ConfigurationManager.AppSettings["EnableMsgRegularLeave"].ToString());
        public static int EnableMsgNoPunch = Int32.Parse(ConfigurationManager.AppSettings["EnableMsgNoPunch"].ToString());

        public static String MessageRegular = ConfigurationManager.AppSettings["MessageRegular"].ToString();
        public static String MessageLate = ConfigurationManager.AppSettings["MessageLate"].ToString();
        public static String MessageAbsent = ConfigurationManager.AppSettings["MessageAbsent"].ToString();
        public static String MessageEarlyLeave = ConfigurationManager.AppSettings["MessageEarlyLeave"].ToString();
        public static String MessageRegularLeave = ConfigurationManager.AppSettings["MessageRegularLeave"].ToString();
        public static String MessageNoPunch = ConfigurationManager.AppSettings["MessageNoPunch"].ToString();

        public static String ServiceID = "SERVICE";
        public static String srcMobile = "MRTComputer";


    }

    public class DBAccess
    {

        public DataTable RetryDetailTable = new DataTable();
        public AuxClass auxObj = new AuxClass();
        //public SqlConnection thisConnection = new SqlConnection(ConfigurationManager.AppSettings["ConnectionString"].ToString());
        String ConnectionString = "";

        public OleDbConnection conn;
        public OleDbCommand QueryCommand;
        public OleDbDataReader QueryReader;

        public int RetryDetailCount = 0;
        public string Operatorkey = null;
        public string NextRetryMin = null;

        Queue<string> QueCommandsExecute;

        // This is a DB access constructor
        public DBAccess(String conString)
        {
            this.ConnectionString = conString;
            this.conn = new OleDbConnection("Provider=Microsoft.Jet.OLEDB.4.0; " +
                                    "Data Source=" + this.ConnectionString);

            DbOpenConnection();

            this.QueryCommand = this.conn.CreateCommand();
            this.QueryReader = null;
            this.QueCommandsExecute = new Queue<String>();           
        }

        public void DbOpenConnection()
        {
            try
            {
                bool CheckOpenConnection = (conn.State == ConnectionState.Open);
                if (!CheckOpenConnection)
                {
                    conn.Open();
                }
            }
            catch (Exception ex)
            {
                conn.Close();
                auxObj.LogWriter(ex.ToString());

            }
        }

        public void DbCloseConnection()
        {
            try
            {
                bool CheckCloseConnection = (conn.State == ConnectionState.Closed);
                if (!CheckCloseConnection)
                    conn.Close();
            }
            catch (Exception ex)
            {
                conn.Close();
                auxObj.LogWriter(ex.ToString());
            }
        }

        public OleDbDataReader returnReader(String CommandText)
        {
            this.QueryCommand.CommandText = CommandText;
            this.QueryReader = this.QueryCommand.ExecuteReader();
            return QueryReader;
        }

        public void executeCommand(String CommandText)
        {
            QueryCommand.CommandText = CommandText;
            QueryCommand.ExecuteNonQuery();
        }

        public void AddInQueue(String CommandText)
        {
            QueCommandsExecute.Enqueue(CommandText);
        }

        public T returnValue<T> (String CommandText)
        {
            T retValue;
            QueryCommand.CommandText = CommandText;
            retValue = (T) QueryCommand.ExecuteScalar();
            return (T)Convert.ChangeType(retValue, typeof(T));
        }

    }

    public class ThreadProcessorClass
    {
        public string[] OutBox;
        public string SMSPostingAddress = ConfigurationManager.AppSettings["SMSPostingAddress"].ToString();
        public ThreadProcessorClass(string[] OutBoxFields)
        {
            OutBox = OutBoxFields;
        }

        public void tWeb()
        {
            string strResponse = "";
            string SMSMessageUrlEncode = "";
            string RequestingUrl = "";
            String SMSMessage = OutBox[2];
            String ReSMSMessage = "";

            //OutBox[0] =dstMN
            //OutBox[1] =srcMN
            //OutBox[2] =msg
            //OutBox[3] =msgID
            //OutBox[4] =ServiceID
            //OutBox[5] =retrycount
            //OutBox[6] =msgStatus
            //OutBox[7] =Operator key

            ReSMSMessage = SMSMessage.Replace("|", Environment.NewLine);
            SMSMessageUrlEncode = HttpUtility.UrlEncode(ReSMSMessage);
            Thread.Sleep(10);
            RequestingUrl = SMSPostingAddress + "&SendFrom=" + OutBox[1] + "&SendTo=880" + OutBox[0] + "&InMSgID=" + OutBox[3] + "&Msg=" + SMSMessageUrlEncode;

            Thread.Sleep(10);

            try
            {
                WebRequest myWebRequest = WebRequest.Create(RequestingUrl);
                WebResponse myWebResponse = myWebRequest.GetResponse();
                Stream ReceiveStream = myWebResponse.GetResponseStream();
                Encoding encode = System.Text.Encoding.GetEncoding("utf-8");
                StreamReader readStream = new StreamReader(ReceiveStream, encode);
                strResponse = readStream.ReadToEnd();
                readStream.Close();
                myWebResponse.Close();
                Thread.Sleep(80);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: from Web Responce:" + ex.ToString());
            }

             OutBox[6] = strResponse.Trim();
        }

    }

    public class AuxClass
    {
        public string LogEnable = ConfigurationManager.AppSettings["LogEnable"].ToString();
        public string LogFilePath = ConfigurationManager.AppSettings["LogFilePath"].ToString();

        public string CurrentTime()
        {
            string CurrentTimeValue = DateTime.Now.ToString("yyyyMMddhhmmssffffff");
            return CurrentTimeValue;
        }
        public void LogWriter(String MessageString)
        {
            try
            {
                if (LogEnable == "1")
                {
                    MessageString = MessageString + " Time:" + CurrentTime() + "\n";
                    Console.WriteLine(MessageString);
                    StreamWriter OurStream;
                    String logfilename = DateTime.Now.ToString("yyyyMMddhh");
                    String path = LogFilePath + logfilename + ".txt";
                    if (File.Exists(path))
                    {
                        System.IO.StreamWriter file = new System.IO.StreamWriter(path, true);
                        file.WriteLine(MessageString);
                        file.Close();

                    }
                    else
                    {
                        OurStream = File.CreateText(path);
                        OurStream.Close();
                        System.IO.StreamWriter file = new System.IO.StreamWriter(path);
                        file.WriteLine(MessageString);
                        file.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                LogWriter("Exception From LogWriter:" + ex.Message);
                Console.WriteLine("Error: from LogWriter:" + ex.ToString());
            }
        }

    }

}
