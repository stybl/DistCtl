﻿namespace DistNode
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using DistCommon;
    using DistCommon.Logging;
    using Newtonsoft.Json;
    using Comm = DistCommon.Comm;
    using Constants = DistCommon.Constants;

    public sealed class Node : IDisposable
    {
        private readonly Config config;
        private Listener listener;
        private DistCommon.Schema.Node schematic;
        private Dictionary<int, Worker> workers;
        private bool constructed;
        private DistCommon.Logging.Logger logger;
        private List<Comm.Reports.Base> reports;

        public Node(string configFilename = Constants.Node.ConfigFilename) 
        {
            string[] dependencies = { configFilename };
            if (new DistCommon.Utils.DepMgr(dependencies).FindMissing().Count != 0)
            {
                throw new DistException("Configuration file not found.");
            }

            try
            {
                this.config = DistCommon.Utils.JFI.GetObject<Config>(configFilename);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                throw new DistException("Configuration file invalid.");
            }

            this.logger = new DistCommon.Logging.Logger(DistCommon.Constants.Node.LogFilename, Source.Node);
            this.constructed = false;
            this.workers = new Dictionary<int, Worker>();
            this.reports = new List<DistCommon.Comm.Reports.Base>();
        }

        public void Initialize()
        {
            if (this.config != null)
            {
                this.logger.Log("Starting up node...");
                try
                {
                    this.logger.Log("Initializing listener...");
                    this.listener = new Listener(this.config.Port, this.ExecuteRequest, this.logger.Log);
                    this.logger.Log(string.Format("Listening on port: {0}", this.config.Port.ToString()));
                    this.logger.Log("Startup complete!");
                    var task = Task.Run(async () => { await this.listener.StartListener(); });
                    task.Wait();
                }
                catch (Exception e)
                {
                    this.Dispose();
                    if (e.GetType() == typeof(AggregateException))
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                    }

                    if (!this.config.EnableLiveErrors)
                    {
                        this.logger.Log(e.StackTrace, Severity.Critical);
                        Environment.Exit(1);
                    }

                    throw;
                }
            }
            else
            {
                throw new DistException("Configuration file invalid.");
            }
        }

        public void Dispose()
        {
            foreach (var worker in this.workers)
            {
                this.Remove(worker.Key);
            }
        }

        private Result Assign(DistCommon.Job.Blueprint blueprint)
        {
            if (this.constructed)
            {
                if (this.schematic.Slots > this.workers.Count)
                {
                    this.workers.Add(blueprint.ID, new Worker(blueprint, this.WorkerExitedHandler));
                    return Result.Success;
                }

                return Result.Fail;
            }

            return Result.NotConstructed;
        }

        private Result Remove(int id)
        {
            if (this.constructed)
            {
                if (this.workers.ContainsKey(id))
                {
                    if (this.workers[id].Awake)
                    {
                        bool temp = this.workers[id].Stop().Result;
                    }

                    this.workers.Remove(id);
                    return Result.Success;
                }

                return Result.NotFound;
            }

            return Result.NotConstructed;
        }

        private Result Wake(int id)
        {
            if (this.constructed)
            {
                if (this.workers.ContainsKey(id))
                {
                    if (!this.workers[id].Awake)
                    {
                        if (this.workers[id].Start().Result)
                        {
                            return Result.Success;
                        }

                        return Result.Fail;
                    }

                    return Result.Invalid;
                }

                return Result.NotFound;
            }

            return Result.NotConstructed;
        }

        private Result Sleep(int id)
        {
            if (this.constructed)
            {
                if (this.workers.ContainsKey(id))
                {
                    if (this.workers[id].Awake)
                    {
                        if (this.workers[id].Stop().Result)
                        {
                            return Result.Success;
                        }

                        return Result.Fail;
                    }

                    return Result.Invalid;
                }

                return Result.NotFound;
            }

            return Result.NotConstructed;
        }

        private Result Construct(DistCommon.Schema.Node schematic)
        {
            if (!this.constructed)
            {
                this.schematic = schematic;
                this.constructed = true;
                return Result.Success;
            }

            return Result.Invalid;
        }

        private Result Reset()
        {
            if (this.constructed)
            {
                foreach (int id in this.workers.Keys.ToList())
                {
                    this.Remove(id);
                }

                this.reports.Clear();
                this.schematic = null;
                this.constructed = false;

                return Result.Success;
            }

            return Result.NotConstructed;
        }

        private void AddReport(Comm.Reports.Base report)
        {
            this.reports.Add(report);
        }

        private List<Comm.Reports.Base> GetReports()
        {
            var temp = this.reports.ToList();
            this.reports.Clear();
            return temp;
        }

        private void WorkerExitedHandler(int id)
        {
            this.AddReport(new Comm.Reports.WorkerExited(id));
            this.logger.Log(string.Format("Worker [{0}] exited unexpectedly", id.ToString()), Severity.Severe);
        }

        private string ExecuteRequest(string requeststr)
        {
            try
            {
                var settings = new JsonSerializerSettings();
                settings.MissingMemberHandling = MissingMemberHandling.Error;

                var baseRequest = JsonConvert.DeserializeObject<Comm.Requests.Base>(requeststr);
                dynamic request = JsonConvert.DeserializeObject(requeststr, baseRequest.RequestType);
                bool supressLog = baseRequest.RequestType == typeof(Comm.Requests.Report);
                if (!supressLog)
                {
                    this.logger.Log(string.Format("Received request [{0}]", baseRequest.RequestType));
                }

                Comm.Responses.Base result = this.HandleRequest(request);

                if (!supressLog)
                {
                    this.logger.Log(string.Format("Operation finished with result [{0}]", result.Result.GetString()));
                }
    
                return JsonConvert.SerializeObject(result);
            }
            catch (JsonException)
            {
                this.logger.Log("Received invalid request", Severity.Warn);
                return Constants.Comm.InvalidResponse;
            }
        }

        private Comm.Responses.Base HandleRequest(Comm.Requests.Assign request)
        {
            return new Comm.Responses.Base(this.Assign(request.Blueprint));
        }

        private Comm.Responses.Base HandleRequest(Comm.Requests.Base request)
        {
            return new Comm.Responses.Base(Result.Success);
        }

        private Comm.Responses.Base HandleRequest(Comm.Requests.Construct request)
        {
            return new Comm.Responses.Base(this.Construct(request.Schematic));
        }

        private Comm.Responses.Base HandleRequest(Comm.Requests.Remove request)
        {
            return new Comm.Responses.Base(this.Remove(request.ID));
        }

        private Comm.Responses.Base HandleRequest(Comm.Requests.Reset request)
        {
            return new Comm.Responses.Base(this.Reset());
        }

        private Comm.Responses.Base HandleRequest(Comm.Requests.Sleep request)
        {
            return new Comm.Responses.Base(this.Sleep(request.ID));
        }

        private Comm.Responses.Base HandleRequest(Comm.Requests.Test request)
        {
            return new Comm.Responses.Base(Result.Success);
        }

        private Comm.Responses.Base HandleRequest(Comm.Requests.Wake request)
        {
            return new Comm.Responses.Base(this.Wake(request.ID));
        }

        private Comm.Responses.Base HandleRequest(Comm.Requests.Report request)
        {
            return new Comm.Responses.Report(this.GetReports());
        }
    }
}
