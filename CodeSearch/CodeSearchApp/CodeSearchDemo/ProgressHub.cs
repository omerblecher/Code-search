using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.ModelBinding;
using Microsoft.AspNet.SignalR;

namespace CodeSearchDemo
{
    public class ProgressHub : Hub
    {
        
        public void CallLongOperation()
        {
           
                
           
        }

        public static void SendMessage(string msg, int count, string conID)
        {
            var hubContext = GlobalHost.ConnectionManager.GetHubContext<ProgressHub>();
            hubContext.Clients.Client(conID).sendMessage(string.Format(msg), count);
        }
        


    }
}