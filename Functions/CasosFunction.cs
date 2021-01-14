using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;


using System.Linq;


using Microsoft.Azure.Cosmos;

namespace Caso.Function
{
    public class Send2EventGridObject
    {
        public string telefono { get; set; }
        public string nombre { get; set; }
        public string vuelo { get; set; }
    }
    public static class CasosFunction
    {
        private static readonly string _endpointUrl = System.Environment.GetEnvironmentVariable("endpointUrl");                
        private static readonly string _primaryKey = System.Environment.GetEnvironmentVariable("primaryKey");
        private static readonly string _databaseId = "Travellers";
        private static readonly string _containerId = "casosinst";
        private static CosmosClient cosmosClient = new CosmosClient(_endpointUrl, _primaryKey);

        [FunctionName("CasosFunction")]
        public static async Task Run([CosmosDBTrigger(
            databaseName: "Travellers",
            collectionName: "casos",
            ConnectionStringSetting = "horaazurecosmosdb_DOCUMENTDB",
            LeaseCollectionName = "leases", CreateLeaseCollectionIfNotExists = true)]IReadOnlyList<Document> input, [CosmosDB(
                databaseName: "Travellers",
                collectionName: "personas",
                ConnectionStringSetting = "horaazurecosmosdb_DOCUMENTDB")] DocumentClient client,
        [EventGrid(TopicEndpointUri = "MyEventGridTopicUriSetting", TopicKeySetting = "MyEventGridTopicKeySetting")]IAsyncCollector<EventGridEvent> outputEvents                
        , ILogger log)
        {

              var container2 = cosmosClient.GetContainer(_databaseId, _containerId);


            if (input != null && input.Count > 0)
            {
                log.LogInformation("Documents modified " + input.Count);
                log.LogInformation("First document Id " + input[0].Id);
            }

            foreach(Document doc in input){
                log.LogInformation("Guardaré el documento en el contenedor 2 y buscaré las personas del vuelo afectado");
                log.LogInformation("doc: "+doc);
                try{
                    await container2.CreateItemAsync<Document>(doc);
     
                    // Búsqueda en la colección de personas
                    Uri collectionUri = UriFactory.CreateDocumentCollectionUri("Travellers", "personas");
                    string vueloBuscar = doc.GetPropertyValue<string>("vuelo");
                    log.LogInformation($"Buscando:"+vueloBuscar);

                    string query2Execute = "SELECT * FROM personas a WHERE a.vuelo = '"+vueloBuscar+"'";

                    IDocumentQuery<Document> query = client.CreateDocumentQuery<Document>(collectionUri,query2Execute)
                        .AsDocumentQuery();

                    while (query.HasMoreResults)
                    {
                        foreach (Document result in await query.ExecuteNextAsync())
                        {
                            log.LogInformation(result.GetPropertyValue<string>("nombre"));
                            Send2EventGridObject dataToSend = new Send2EventGridObject()
                            {
                                telefono = result.GetPropertyValue<string>("telefono"),
                                nombre = result.GetPropertyValue<string>("nombre"),
                                vuelo = vueloBuscar
                            };

                            var myEvent = new EventGridEvent(doc.Id+"-"+result.Id,result.ResourceId,dataToSend,"Integration.CovidAlerts", DateTime.UtcNow, "1.0");
                            await outputEvents.AddAsync(myEvent);
                        }
                    }                        
                }
                catch (Exception e) {
                        log.LogInformation("Excepción guardando en el contenedor o en la búsqueda: "+e);
                }
                
            }            
        }
    }
}
