﻿namespace DistCtl
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface IController
    {
        Task<int> Add(DistCommon.Job.Blueprint job);

        Task<int> Add(DistCommon.Job.Blueprint job, int nodeID);

        Task<int> Add(DistCommon.Schema.Node schematic);

        Task<int> Assign(int jobID);

        Task<int> Assign(int jobID, int nodeID);

        JobInfo GetJob(int jobID);

        Dictionary<int, JobInfo> GetJob();

        NodeInfo GetNode(int nodeID);

        Dictionary<int, NodeInfo> GetNode();

        Task<int> Remove(int nodeID);

        Task<int> Remove(int jobID, int nodeID);

        Task<int> Sleep(int jobID);

        Task<int> Wake(int jobID);
    }
}