using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BulkSMSPostingApplication2
{
    public class Employee
    {
        String UserID;
        String BadgeNumber;
        String Name;
        Shift employeeShift;
        String Mobile;

        public int checkInValue;  // 1 for regular checkin  2 for late checkin 0 for nocheck in
        public int checkOutValue; // 1 for regular checkout 2 for early chekout 0 for nocheckout

        int checkinMessageSend;
        int checkOutMessageSend;
        public int absentMessageSend;
        public int nopunchMessageSend;

        public Employee(string UserID, string BadgeNumber, string Name, Shift empShift,String Mobile)
        {
            // TODO: Complete member initialization
            this.UserID = UserID;
            this.BadgeNumber = BadgeNumber;
            this.Name = Name;
            this.employeeShift = empShift;
            this.Mobile = Mobile;

            this.checkInValue = 0;
            this.checkOutValue = 0;
            this.checkinMessageSend = 0;
            this.checkOutMessageSend = 0;
        }

        public void CheckIN(DateTime checkTime, DBAccess objDB)
        {
            int CurrentShiftValue = employeeShift.getCurrentShift(checkTime);
            if (CurrentShiftValue == 1)// Regular checkin time
            {
                this.checkInValue = 1;
                if (this.checkinMessageSend != 1)
                {
                    saveInDB(QueryInProgram.EnableMsgRegular, QueryInProgram.MessageRegular, objDB);
                    this.checkinMessageSend = 1;
                }
                
            }
            else if (CurrentShiftValue == 2)// late check in time
            {
                this.checkInValue = 2;
                if (this.checkinMessageSend != 1)
                {
                    saveInDB(QueryInProgram.EnableMsgLate, QueryInProgram.MessageLate, objDB);
                    this.checkinMessageSend = 1;
                }
            }
            else if (CurrentShiftValue == 3)// Absent time or Early checkout time
            {
                if (this.checkInValue > 0)
                    this.checkOutValue = 2;
                if (this.checkOutMessageSend != 1)
                {
                    saveInDB(QueryInProgram.EnableMsgEarlyLeave, QueryInProgram.MessageEarlyLeave, objDB);
                    this.checkOutMessageSend = 1;
                }
            }
            else if (CurrentShiftValue == 4)//Early Check out time
            {
                if (this.checkInValue > 0)
                    this.checkOutValue = 2;
                if (this.checkOutMessageSend != 1)
                {
                    saveInDB(QueryInProgram.EnableMsgEarlyLeave, QueryInProgram.MessageEarlyLeave, objDB);
                    this.checkOutMessageSend = 1;
                }
            }
            else if (CurrentShiftValue == 5)//Regular check out time
            {
                if (this.checkInValue > 0)
                    this.checkOutValue = 1;
                if (this.checkOutMessageSend != 1)
                {
                    saveInDB(QueryInProgram.EnableMsgRegularLeave, QueryInProgram.MessageRegularLeave, objDB);
                    this.checkOutMessageSend = 1;
                }
            }
        }

        public void saveInDB(int AllowSave, String Message, DBAccess objDB)
        {
            DateTime curTime = DateTime.Now;
            String CommandText;
            String msg;
            String IN_MSG_ID;
            Random ran = new Random();
            if (AllowSave == 1)
            {              
                msg = "Name " + this.Name + " ID: " + this.BadgeNumber + " " + Message;
                IN_MSG_ID = this.Mobile + curTime.ToString("yyyyMMddHHmmss") + ran.Next(1,99);

    
                CommandText = "Insert into SMSOutbox(srcMn, dstMN,writeTime,Schedule, msg, msgStatus, retrycount,ServiceID,IN_MSG_ID) " +
                        " values( '" + QueryInProgram.srcMobile + "', '" + this.Mobile + "', #" + curTime.ToString("yyyy-MM-dd HH:mm:ss") + "#, #"
                        + curTime.ToString("yyyy-MM-dd HH:mm:ss") + "#, '" + msg + "','QUE', 3, '" + QueryInProgram.ServiceID + "','" + IN_MSG_ID + "' )";

                objDB.executeCommand(CommandText);   
            }                    
        }
    }

    public class Shift
    {
        int schClassID;
        String schName;
        DateTime StartTime;
        DateTime EndTime;
        int LateMinutes;
        int EarlyMinutes;
        DateTime CheckInTime1;
        DateTime CheckInTime2;
        DateTime CheckOutTime1;
        DateTime CheckOutTime2;
        public List<Employee> ListEmployees;

        public Shift(int schClassID,String schName, DateTime StartTime, DateTime EndTime,int LateMinutes,int EarlyMinutes,
            DateTime CheckInTime1,DateTime CheckInTime2,DateTime CheckOutTime1,DateTime CheckOutTime2)
        {
            this.schClassID = schClassID;
            this.schName = schName;
            this.StartTime = StartTime;
            this.EndTime = EndTime;
            this.LateMinutes = LateMinutes;
            this.EarlyMinutes = EarlyMinutes;
            this.CheckInTime1 = CheckInTime1;
            this.CheckInTime2 = CheckInTime2;
            this.CheckOutTime1 = CheckOutTime1;
            this.CheckOutTime2 = CheckOutTime2;
            this.ListEmployees = new List<Employee>();
        }

        public int getCurrentShift(DateTime shiftTime)
        {
            int retShift =0;
            DateTime t1 = StartTime.AddMinutes(LateMinutes);
            DateTime t2 = EndTime.AddMinutes(-EarlyMinutes);
            if (shiftTime.TimeOfDay >= CheckInTime1.TimeOfDay && shiftTime.TimeOfDay <= t1.TimeOfDay) // Regular checkin time
                retShift = 1;
            else if (shiftTime.TimeOfDay > t1.TimeOfDay && shiftTime.TimeOfDay <= CheckInTime2.TimeOfDay) // late check in time
                retShift = 2;
            else if (shiftTime.TimeOfDay > CheckInTime2.TimeOfDay && shiftTime.TimeOfDay <= CheckOutTime1.TimeOfDay) // Absent time or Early checkout time
                retShift = 3;
            else if (shiftTime.TimeOfDay > CheckOutTime1.TimeOfDay && shiftTime.TimeOfDay <= t2.TimeOfDay) //Early Check out time
                retShift = 4;
            else if (shiftTime.TimeOfDay > t2.TimeOfDay && shiftTime.TimeOfDay <= CheckOutTime2.TimeOfDay) //Regular check out time
                retShift = 5;
            else if (shiftTime.TimeOfDay > CheckOutTime2.TimeOfDay)
                retShift = 6;
            else
                retShift = 0;
            return retShift;
        }
    }
}
