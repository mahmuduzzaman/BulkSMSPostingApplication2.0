using System;
using System.Collections.Generic;
using System.Collections;
using System.Xml;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Data;
using System.Data.OleDb;
using System.Threading;
using System.Configuration;
using System.IO;
using System.Web;
using System.Net;
using System.Data.SqlClient;

namespace BulkSMSPostingApplication2
{           
    //Class for creating SMS from Check in out
    public class CheckInProcess
    {
        DBAccess objDBMain;
        DBAccess objDBSMS;
        int TotalUsers;
        int CurrentDay;

        DateTime lastTime;
        DateTime curTime;

        IDataReader QueryReader;
        public Hashtable htUsers; 
        public Hashtable htShifts;

        public CheckInProcess(DBAccess objDBAccess, DBAccess objDB2)
        {
            this.objDBMain = objDBAccess;
            this.objDBSMS = objDB2;
            this.lastTime = DateTime.Now.AddMinutes(-1);
            this.CurrentDay = DateTime.Now.Day;

            this.htUsers = new Hashtable();
            this.htShifts = new Hashtable();
            this.QueryReader = null;
            
            PopulateShifts();
            PopulateUsers();            
        }

        void PopulateUsers()
        {
            htUsers.Clear();
            QueryReader = objDBMain.returnReader(QueryInProgram.UserPopulateSQL);//UserID, Badgenumber,Name,SCHCLASSID, PAGER

            while (QueryReader.Read())
            {
                String UserID = QueryReader["USERID"].ToString();
                String BadgeNumber = QueryReader["Badgenumber"].ToString();
                String Name = QueryReader["Name"].ToString();
                int ShiftNumber =  Int32.Parse(QueryReader["SCHCLASSID"].ToString());                    
                Shift currentShift =(Shift) htShifts[ShiftNumber];
                    
                String Mobile = QueryReader["PAGER"].ToString();
                Employee newEmployee = new Employee(UserID, BadgeNumber, Name, currentShift,Mobile);
                htUsers.Add(UserID,newEmployee );
                currentShift.ListEmployees.Add(newEmployee);
            }

            QueryReader.Close();
            TotalUsers = htUsers.Count;
        }

        void PopulateShifts()
        {
            htShifts.Clear();
            QueryReader = objDBMain.returnReader(QueryInProgram.ShiftPopulate);//schClassid, schName,StartTime,EndTime,LateMinutes
            //EarlyMinutes, CheckInTime1, CheckInTime2, CheckOutTime1, CheckOutTime2

            while (QueryReader.Read())
            {
                int schClassID = Int32.Parse(QueryReader["schClassid"].ToString());
                String schName = QueryReader["schName"].ToString();
                DateTime StartTime = DateTime.Parse(QueryReader["StartTime"].ToString());
                DateTime EndTime = DateTime.Parse(QueryReader["EndTime"].ToString());
                int LateMinutes = Int32.Parse(QueryReader["LateMinutes"].ToString());
                int EarlyMinutes = Int32.Parse(QueryReader["EarlyMinutes"].ToString());
                DateTime CheckInTime1 = DateTime.Parse(QueryReader["CheckInTime1"].ToString());
                DateTime CheckInTime2 = DateTime.Parse(QueryReader["CheckInTime2"].ToString());
                DateTime CheckOutTime1 = DateTime.Parse(QueryReader["CheckOutTime1"].ToString());
                DateTime CheckOutTime2 = DateTime.Parse(QueryReader["CheckOutTime2"].ToString());

                htShifts.Add(schClassID, new Shift(schClassID, schName, StartTime, EndTime, LateMinutes, EarlyMinutes, CheckInTime1, CheckInTime2, CheckOutTime1, CheckOutTime2));
            }

            QueryReader.Close();
        }

        void CheckShifts() 
        {
            ICollection key = htShifts.Keys;

            foreach (int k in key)
            {
                Shift curShift = (Shift)htShifts[k];
                int ShiftNumber = curShift.getCurrentShift(curTime);
                List<Employee> lstEmp = curShift.ListEmployees;

                if (ShiftNumber == 3) //Checking will be for Absense
                {                   
                    foreach (var item in lstEmp)
                    {
                        if (item.checkInValue == 0 && item.absentMessageSend==0)
                        {
                            item.saveInDB(QueryInProgram.EnableMsgAbsent, QueryInProgram.MessageAbsent, objDBSMS);
                            item.absentMessageSend = 1;
                        }                            
                    }                        
                }
                else if (ShiftNumber == 6)
                {
                    foreach (var item in lstEmp)
                    {
                        if (item.checkOutValue == 0 && item.nopunchMessageSend == 0 && item.checkInValue > 0)
                        {
                            item.saveInDB(QueryInProgram.EnableMsgNoPunch, QueryInProgram.MessageNoPunch, objDBSMS);
                            item.nopunchMessageSend = 1;
                        }
                    }      
                }
            }
        }
       
        public void ThreadRun()
        {
            while (true)
            {
                curTime = DateTime.Now;
                
                // Day changed
                if (CurrentDay != curTime.Day)
                {
                    CurrentDay = curTime.Day;
                    PopulateShifts();
                    PopulateUsers();
                }

                //Check User Count
                int CountUser = objDBMain.returnValue<int>(QueryInProgram.UserCountSql);
                if (CountUser != TotalUsers)
                {
                    PopulateShifts();
                    PopulateUsers();
                }
                    
                String QueryCheckinOLe = "SELECT USERID,CHECKTIME FROM CHECKINOUT where CHECKTIME > #"
                    + lastTime.ToString("yyyy-MM-dd HH:mm:ss") + "# and CHECKTIME <= #"
                    + curTime.ToString("yyyy-MM-dd HH:mm:ss") + "#";

                String QueryCheckinSql = "SELECT USERID,CHECKTIME FROM CHECKINOUT where CHECKTIME > '"
                    + lastTime.ToString("yyyy-MM-dd HH:mm:ss") + "' and CHECKTIME <= '"
                    + curTime.ToString("yyyy-MM-dd HH:mm:ss") + "'";

                QueryReader = objDBMain.returnReader(QueryCheckinSql);

                while (QueryReader.Read())
                {
                    //Get the Employee
                    String UserID = QueryReader["USERID"].ToString();
                    DateTime checkTime = DateTime.Parse(QueryReader["CHECKTIME"].ToString());
                    Employee empCurrent = (Employee)htUsers[UserID];
                    empCurrent.CheckIN(checkTime, objDBSMS);
                }


                QueryReader.Close();

                //Checking the Shifts for Absense or No punch
                CheckShifts();

                lastTime = curTime;//setting Current time to last time to start from here

                Thread.Sleep(1 *   // minutes to sleep
                 60 *   // seconds to a minute
                 1000); // milliseconds to a second
            }            
        }
    }
    //Class for Sending web request from SMS outbox
    public class SMSProcess
    {
        int count;
        int recordCount;
        public ManualResetEvent finished = new ManualResetEvent(false);
        List<string[]> OutboxList = new List<string[]>();
        AuxClass auxObj = new AuxClass();
        public string OutboxWhereClause = ConfigurationManager.AppSettings["OutboxWhereClause"].ToString();
        public string OutboxCountClause = ConfigurationManager.AppSettings["OutboxCountClause"].ToString();

        public int HasDataInSMS;

        DBAccess objDBAccess;

        public SMSProcess(DBAccess objDBAccess)
        {
            this.objDBAccess = objDBAccess;
            this.count = 0;
            this.recordCount = 0;
        }
        
        public void ThreadRun()
        {
            while (true)
            {
                try
                {
                    //Initialization
                    this.count = 0;
                    this.recordCount = 0;
                    String CommandText;
                    HasDataInSMS = 0;
                    //TopNumber="0";
                    finished.Reset();
                    string TopNumber = ConfigurationManager.AppSettings["TopNumber"].ToString();

                    CommandText = "SELECT count(msgID) FROM SMSOutbox " + OutboxCountClause + "";
                    recordCount = objDBAccess.returnValue<int>(CommandText);
                    if (recordCount > Int32.Parse(TopNumber))
                    {
                        Console.WriteLine("TopNumber:" + TopNumber);
                        recordCount = Int32.Parse(TopNumber);
                        Console.WriteLine("recordCount:" + recordCount);
                        TopNumber = Convert.ToString(recordCount);
                        Console.WriteLine("TopNumber:" + TopNumber);
                    }
                    else
                    {
                        TopNumber = Convert.ToString(recordCount);
                        Console.WriteLine("TopNumber:" + TopNumber);
                    }

                    
                    if (recordCount > 0)
                    {

                        CommandText = "SELECT top " + TopNumber + " msgID,dstMN,srcMN,msg,IN_MSG_ID,ServiceID,retrycount,msgStatus FROM SMSOutbox  " + OutboxWhereClause + "";
                        IDataReader OutboxQueryReader = objDBAccess.returnReader(CommandText);

                        //dstMN, srcMN,msg,msgID,ServiceID,retrycount,msgStatus
                        while (OutboxQueryReader.Read())
                        {
                            string[] OutBoxFields = new string[8];
                            OutBoxFields[0] = OutboxQueryReader["dstMN"].ToString();
                            OutBoxFields[1] = OutboxQueryReader["srcMN"].ToString();
                            OutBoxFields[2] = OutboxQueryReader["msg"].ToString();
                            OutBoxFields[3] = OutboxQueryReader["IN_MSG_ID"].ToString();
                            OutBoxFields[4] = OutboxQueryReader["ServiceID"].ToString();
                            OutBoxFields[5] = OutboxQueryReader["retrycount"].ToString();
                            OutBoxFields[6] = OutboxQueryReader["msgStatus"].ToString();
                            OutBoxFields[7] = OutboxQueryReader["msgID"].ToString();

                            HasDataInSMS++;
                            //OutBoxFields[8] = DateTime.Now.ToString("yyyyMMddhhmmssffffff");
                            Thread.Sleep(2);
                            ThreadProcessorClass objThreadProcessorClass = new ThreadProcessorClass(OutBoxFields);
                            ThreadPool.QueueUserWorkItem(new WaitCallback(OutboxThreadProcessor), objThreadProcessorClass);
                        }

                        OutboxQueryReader.Close();
                        //OutboxQueryCommand.Dispose();

                        finished.WaitOne();

                        if (HasDataInSMS > 0)
                        {
                                foreach (string[] NewOutBoxFields in OutboxList)
                                {


                                    XmlDocument xDoc = new XmlDocument();
                                    //load up the xml from the location 
                                    xDoc.LoadXml(NewOutBoxFields[6]);
                                    //xDoc.Load(NewOutBoxFields[6]);
                                    // Get elements
                                    XmlNodeList NewStatusXML = xDoc.GetElementsByTagName("Status");
                                    XmlNodeList IN_MSG_IDXML = xDoc.GetElementsByTagName("InMsgID");
                                    XmlNodeList StatusCodeXML = xDoc.GetElementsByTagName("StatusCode");

                                    string NewStatus = NewStatusXML[0].InnerText;
                                    string IN_MSG_ID = IN_MSG_IDXML[0].InnerText;
                                    string StatusCode = StatusCodeXML[0].InnerText;

                                    if (NewStatus.Trim() == "POSTED")
                                    {
                                        //(srcMn, dstMN,writeTime,Schedule, msg, msgStatus, retrycount,ServiceID,IN_MSG_ID)
                                        CommandText = "delete from SMSOutbox " +
                                            " where IN_MSG_ID ='" + IN_MSG_ID + "' ";
                                        objDBAccess.executeCommand(CommandText);
                                    }
                                    else
                                    {
                                        //UpdateQueryCommand.CommandText = "UPDATE SMSOutbox SET msgStatus ='POSTED',sentTime=CURRENT_TIMESTAMP,msgID='" + NewMsgID.Trim() + "',Remarks='" + NewDescription.Trim() + "' WHERE ID ='" + NewOutBoxFields[7] + "' ";

                                        int NextRetryCount = Convert.ToInt16(NewOutBoxFields[5]) - 1;
                                        CommandText = "UPDATE SMSOutbox set  retrycount = " + NextRetryCount +
                                                " where IN_MSG_ID ='" + IN_MSG_ID + "' ";
                                        objDBAccess.executeCommand(CommandText);

                                    }

                                    string SendingStatus = "Sender:" + NewOutBoxFields[1] + "| Destination:" + NewOutBoxFields[0] + "|Status:" + NewStatus + "|Message ID:" + IN_MSG_ID.Trim() + " | Message:" + NewOutBoxFields[2] + "\n";
                                    Console.WriteLine(SendingStatus);
                                    auxObj.LogWriter(SendingStatus);
                                }

                                OutboxList.Clear();
                            }
                            // End has Rows

                        TopNumber = null;
                    }
                    else
                    {
                        Console.WriteLine("Application running. No data available to send.Current Time:" + auxObj.CurrentTime() + " ");
                        Thread.Sleep(300);
                    }
                }
                catch (SqlException ex)
                {
                    auxObj.LogWriter("SQL Exception from Send Data: " + ex.Message);
                    Console.WriteLine("SQL Exception from Send Data: " + ex);
                }
                catch (Exception ex)
                {
                    auxObj.LogWriter("Exception from Send Data: " + ex.Message);
                    Console.WriteLine("Exception from Send Data: " + ex);
                }
                finally
                {
                    OutboxList.Clear();
                    //objDBAccess.DbCloseConnection();
                }

                Thread.Sleep(1);
            }
        }
        public void OutboxThreadProcessor(Object obj)
        {
            string[] OutBox = ((ThreadProcessorClass)obj).OutBox;
            string SMSPostingAddress = ConfigurationManager.AppSettings["SMSPostingAddress"].ToString();

            try
            {
                ((ThreadProcessorClass)obj).tWeb();
                OutBox = ((ThreadProcessorClass)obj).OutBox;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception from Thread Processor: " + ex.Message);
            }
            finally
            {

                // Decrement the counter to indicate the work item is done.
                OutboxList.Add(OutBox);
                Interlocked.Increment(ref count);
                //Console.WriteLine(count);
                if (count == recordCount)
                {
                    //Console.WriteLine(count);
                    finished.Set();
                    count = 0;
                    recordCount = 0;
                }
            }
        }

    }

    class Program
    {
        public string SMSPostingAddress = ConfigurationManager.AppSettings["SMSPostingAddress"].ToString();
        public string TopNumber = ConfigurationManager.AppSettings["TopNumber"].ToString();
        public string DefaultNextRetryMin = ConfigurationManager.AppSettings["DefaultNextRetryMin"].ToString();
        

        AuxClass auxObj = new AuxClass();

        public static void Main(string[] args)
        {
            //DBAccess objDBAccess = new DBAccess(QueryInProgram.ConnectionSMSDB, "OleDb"); //Access Connections
            //DBAccess objDBCheck = new DBAccess(QueryInProgram.ConnectionMainDB, "OleDb");

            DBAccess objDBSMS = new DBAccess(QueryInProgram.ConnectionSmsSQL, "SqlClient");
            DBAccess objDBCheck = new DBAccess(QueryInProgram.ConnectionMainSQL, "SqlClient");
            //DBAccess objDBCheck2 = new DBAccess(QueryInProgram.ConnectionSMSDB);
            Program instance = new Program();

            try
            {   
                
                SMSProcess smsClass = new SMSProcess(objDBSMS);
                CheckInProcess checkClass = new CheckInProcess(objDBCheck, objDBSMS);

                Thread smsThread = new Thread(new ThreadStart(smsClass.ThreadRun));
                Thread checkThread = new Thread(new ThreadStart(checkClass.ThreadRun));

                smsThread.Start();
                checkThread.Start();

                smsThread.Join();   // Join both threads with no timeout                   
                checkThread.Join();

            }
            catch (Exception ex)
            {
                instance.auxObj.LogWriter("Exception from main method: " + ex.Message);
                Console.WriteLine("Exception from main method:" + ex.Message);
                Environment.Exit(0);
            }
        }
        
    }
}