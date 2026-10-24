using Json.Models;
using Json.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Json.Repositories
{
    public class CustomerRepository
    {
        private readonly JsonFileStore jsonFile;

        public CustomerRepository(JsonFileStore jsonFile)
        {
            this.jsonFile = jsonFile;
        }

        public async Task<Customer?> GetByIdAsync(int id,CancellationToken token)
        {
            await SimulatedDelay.WaitAsync(
                "Customer Repositry => Open Connection",
                200,200,
                token
                );

            await SimulatedDelay.WaitAsync(
                "Customer Repositry => Execute Query",
                300,600,
                token
                );

            var customers = await jsonFile.ReadAllAsync<Customer>(DataFiles.Customers,token);

            var Result = customers.FirstOrDefault(c => c.Id == id);

            if(Result is null)
            {
                Logging.Info($"CustomerRepository -> Customer #{id} not found.");
                return null;
            }
            Logging.Success($"CustomerRepository->Customer #{id} found.");
            return Result;
        }

        public Customer? GetById(int id)
        {
            var customers = jsonFile.ReadAll<Customer>(DataFiles.Customers);
            return customers.FirstOrDefault<Customer>(c => c.Id == id);
        }
    }
}
