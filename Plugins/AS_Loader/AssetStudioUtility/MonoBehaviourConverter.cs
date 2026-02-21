using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetStudio
{
    public static class MonoBehaviourConverter
    {
        public static TypeTree ConvertToTypeTree(this MonoBehaviour m_MonoBehaviour)
        {
            var m_Type = new TypeTree();
            m_Type.m_Nodes = new List<TypeTreeNode>();
            var helper = new SerializedTypeHelper(m_MonoBehaviour.version);
            helper.AddMonoBehaviour(m_Type.m_Nodes, 0);

            if (m_MonoBehaviour.m_Script.TryGet(out var m_Script))
            {
                Type typeDef = null;

                // Для WebGL - пытаемся получить определение типа из MonoScript данных
                // Метод зависит от того, как реализована загрузка сборок в WebGL
                typeDef = GetTypeFromMonoScript(m_Script);

                if (typeDef != null)
                {
                    var typeDefinitionConverter = new TypeDefinitionConverter(typeDef, helper, 1);
                    m_Type.m_Nodes.AddRange(typeDefinitionConverter.ConvertToTypeTreeNodes());
                }
                else
                {
                    switch (m_Script.m_ClassName)
                    {
                        case "CubismModel":
                            helper.AddMonoCubismModel(m_Type.m_Nodes, 1);
                            break;
                        case "CubismMoc":
                            helper.AddMonoCubismMoc(m_Type.m_Nodes, 1);
                            break;
                        case "CubismFadeController":
                            helper.AddMonoCubismFadeController(m_Type.m_Nodes, 1);
                            break;
                        case "CubismFadeMotionList":
                            helper.AddMonoCubismFadeList(m_Type.m_Nodes, 1);
                            break;
                        case "CubismFadeMotionData":
                            helper.AddMonoCubismFadeData(m_Type.m_Nodes, 1);
                            break;
                        case "CubismExpressionController":
                            helper.AddMonoCubismExpressionController(m_Type.m_Nodes, 1);
                            break;
                        case "CubismExpressionList":
                            helper.AddMonoCubismExpressionList(m_Type.m_Nodes, 1);
                            break;
                        case "CubismExpressionData":
                            helper.AddMonoCubismExpressionData(m_Type.m_Nodes, 1);
                            break;
                        case "CubismDisplayInfoParameterName":
                            helper.AddMonoCubismDisplayInfo(m_Type.m_Nodes, 1);
                            break;
                        case "CubismDisplayInfoPartName":
                            helper.AddMonoCubismDisplayInfo(m_Type.m_Nodes, 1);
                            break;
                        case "CubismPosePart":
                            helper.AddMonoCubismPosePart(m_Type.m_Nodes, 1);
                            break;
                    }
                }
            }
            return m_Type;
        }

        private static Type GetTypeFromMonoScript(MonoScript m_Script)
        {
            try
            {

                string fullTypeName = $"{m_Script.m_Namespace}.{m_Script.m_ClassName}";


                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var type = assembly.GetType(fullTypeName);
                        if (type != null)
                            return type;
                    }
                    catch
                    {

                    }
                }

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var type = assembly.GetTypes()
                            .FirstOrDefault(t => t.Name == m_Script.m_ClassName);
                        if (type != null)
                            return type;
                    }
                    catch
                    {

                    }
                }
            }
            catch
            {
            }

            if (TypeCache.TryGetValue(m_Script.m_ClassName, out Type cachedType))
                return cachedType;

            return null;
        }

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
    }
}
