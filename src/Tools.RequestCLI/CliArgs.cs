using Dorc.ApiModel;
using System;
using System.Text.Json;

namespace Tools.RequestCLI
{
    internal class CliArgs
    {
        private RequestDto _request;
        public CliArgs(string[] args)
        {
            _request = new RequestDto();
            ParseArguments(args);
        }
        
        public RequestDto Request => _request;
        public bool Wait { set; get; }
        
        private void ParseArguments(string[] args)
        {
            _request = new RequestDto();
            foreach (var strArgument in args)
                if (strArgument.ToLower().Contains("/project:"))
                    _request.Project = strArgument.Split(':')[1];
                else if (strArgument.ToLower().Contains("/targetenv:"))
                    _request.Environment = strArgument.Split(':')[1];
                else if (strArgument.ToLower().Contains("/buildtext:"))
                    _request.BuildText = strArgument.Split(':')[1];
                else if (strArgument.ToLower().Contains("/buildnum:"))
                    _request.BuildNum = strArgument.Split(':')[1];
                else if (strArgument.ToLower().Contains("/components:"))
                    _request.Components = strArgument.Split(':')[1].Split(';');
                else if (strArgument.ToLower().Contains("/pinned:"))
                    _request.Pinned = Boolean.Parse(strArgument.Split(':')[1]); 
                else if (strArgument.ToLower().Contains("/wait:"))
                    Wait = Boolean.Parse(strArgument.Split(':')[1]);
                else if (strArgument.ToLower().Contains("/builduri:"))
                    _request.BuildUrl = strArgument.Split(':')[1] + ":" + strArgument.Split(':')[2];
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(Request);
        }
    }

}
