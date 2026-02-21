using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AssetStudio
{
    public class TypeDefinitionConverter
    {
        private readonly Type TypeDef;
        private readonly SerializedTypeHelper Helper;
        private readonly int Indent;
        private readonly Dictionary<string, Type> genericTypeMap = new Dictionary<string, Type>();

        public TypeDefinitionConverter(Type type, SerializedTypeHelper helper, int indent)
        {
            TypeDef = type;
            Helper = helper;
            Indent = indent;
        }

        public TypeDefinitionConverter(Type type, SerializedTypeHelper helper, int indent,
                                      Dictionary<string, Type> genericArguments) : this(type, helper, indent)
        {
            genericTypeMap = genericArguments ?? new Dictionary<string, Type>();
        }

        public List<TypeTreeNode> ConvertToTypeTreeNodes()
        {
            var nodes = new List<TypeTreeNode>();

            var baseTypes = new Stack<Type>();
            var lastBaseType = TypeDef.BaseType;

            while (lastBaseType != null && !UnitySerializationLogic.IsNonSerialized(lastBaseType))
            {
                baseTypes.Push(lastBaseType);
                lastBaseType = lastBaseType.BaseType;
            }

            while (baseTypes.Count > 0)
            {
                var typeReference = baseTypes.Pop();
                foreach (var field in GetSerializableFields(typeReference))
                {
                    if (!IsHiddenByParentClass(baseTypes, field, TypeDef))
                    {
                        nodes.AddRange(ProcessingField(field));
                    }
                }
            }

            foreach (var field in GetSerializableFields(TypeDef))
            {
                nodes.AddRange(ProcessingField(field));
            }

            return nodes;
        }

        private bool WillUnitySerialize(FieldInfo fieldInfo)
        {
            try
            {
                var fieldType = ResolveGenericType(fieldInfo.FieldType);

                // Проверяем, является ли тип UnityEngine.Object
                if (!typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
                {
                    // Проверяем рекурсивные ссылки
                    if (fieldType == fieldInfo.DeclaringType)
                    {
                        return false;
                    }
                }

                return UnitySerializationLogic.WillUnitySerialize(fieldInfo, genericTypeMap);
            }
            catch (Exception ex)
            {
                throw new Exception($"Exception while processing {fieldInfo.FieldType.FullName} {fieldInfo.Name}, error {ex.Message}");
            }
        }

        private static bool IsHiddenByParentClass(IEnumerable<Type> parentTypes, FieldInfo fieldInfo, Type processingType)
        {
            return processingType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(f => f.Name == fieldInfo.Name) ||
                   parentTypes.Any(t => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   .Any(f => f.Name == fieldInfo.Name));
        }

        private IEnumerable<FieldInfo> GetSerializableFields(Type type)
        {
            var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return type.GetFields(bindingFlags)
                .Where(WillUnitySerialize)
                .Where(f => UnitySerializationLogic.IsSupportedCollection(f.FieldType) ||
                           !f.FieldType.IsGenericType ||
                           ShouldImplementIDeserializable(f.FieldType));
        }

        private Type ResolveGenericType(Type type)
        {
            if (!type.IsGenericType || !type.IsGenericTypeDefinition)
                return type;

            if (type.IsGenericParameter)
            {
                if (genericTypeMap.TryGetValue(type.Name, out var resolvedType))
                    return resolvedType;
                return type;
            }

            var genericArguments = type.GetGenericArguments();
            var resolvedArguments = new Type[genericArguments.Length];

            for (int i = 0; i < genericArguments.Length; i++)
            {
                resolvedArguments[i] = ResolveGenericType(genericArguments[i]);
            }

            return type.GetGenericTypeDefinition().MakeGenericType(resolvedArguments);
        }

        private List<TypeTreeNode> ProcessingField(FieldInfo fieldInfo)
        {
            var fieldType = ResolveGenericType(fieldInfo.FieldType);
            return TypeToTypeTreeNodes(fieldType, fieldInfo.Name, Indent, false);
        }

        private static bool IsStruct(Type type)
        {
            return type.IsValueType && !IsEnum(type) && !type.IsPrimitive;
        }

        private static bool IsEnum(Type type)
        {
            return type.IsEnum;
        }

        private static bool RequiresAlignment(Type type)
        {
            if (type == typeof(bool) || type == typeof(char) || type == typeof(sbyte) ||
                type == typeof(byte) || type == typeof(short) || type == typeof(ushort))
                return true;

            return UnitySerializationLogic.IsSupportedCollection(type);
        }

        private static bool IsSystemString(Type type)
        {
            return type == typeof(string);
        }

        private List<TypeTreeNode> TypeToTypeTreeNodes(Type type, string name, int indent, bool isElement)
        {
            var align = false;

            if (!IsStruct(TypeDef) || !typeof(UnityEngine.Object).IsAssignableFrom(TypeDef))
            {
                if (IsStruct(type) || RequiresAlignment(type))
                {
                    align = true;
                }
            }

            var nodes = new List<TypeTreeNode>();

            if (type.IsPrimitive)
            {
                var primitiveName = GetPrimitiveTypeName(type);
                if (isElement)
                {
                    align = false;
                }
                nodes.Add(new TypeTreeNode(primitiveName, name, indent, align));
            }
            else if (IsSystemString(type))
            {
                Helper.AddString(nodes, name, indent);
            }
            else if (IsEnum(type))
            {
                nodes.Add(new TypeTreeNode("SInt32", name, indent, align));
            }
            else if (type.IsArray)
            {
                var elementType = type.GetElementType();
                nodes.Add(new TypeTreeNode(type.Name, name, indent, align));
                Helper.AddArray(nodes, indent + 1);
                nodes.AddRange(TypeToTypeTreeNodes(elementType, "data", indent + 2, true));
            }
            else if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                Helper.AddPPtr(nodes, type.Name, name, indent);
            }
            else if (IsSerializableUnityType(type))
            {
                ProcessUnityType(nodes, type, name, indent);
            }
            else
            {
                nodes.Add(new TypeTreeNode(type.Name, name, indent, align));

                // Рекурсивная обработка вложенных типов
                if (!type.IsGenericParameter && !type.IsInterface)
                {
                    var typeDefinitionConverter = new TypeDefinitionConverter(type, Helper, indent + 1, genericTypeMap);
                    nodes.AddRange(typeDefinitionConverter.ConvertToTypeTreeNodes());
                }
            }

            return nodes;
        }

        private static string GetPrimitiveTypeName(Type type)
        {
            if (type == typeof(bool)) return "bool";
            if (type == typeof(byte)) return "UInt8";
            if (type == typeof(sbyte)) return "SInt8";
            if (type == typeof(short)) return "SInt16";
            if (type == typeof(ushort)) return "UInt16";
            if (type == typeof(int)) return "SInt32";
            if (type == typeof(uint)) return "UInt32";
            if (type == typeof(long)) return "SInt64";
            if (type == typeof(ulong)) return "UInt64";
            if (type == typeof(char)) return "char";
            if (type == typeof(double)) return "double";
            if (type == typeof(float)) return "float";

            throw new NotSupportedException($"Unsupported primitive type: {type.Name}");
        }

        private static bool IsSerializableUnityType(Type type)
        {
            // Проверяем распространенные сериализуемые типы Unity
            string[] unityTypes = {
                "UnityEngine.AnimationCurve",
                "UnityEngine.Gradient",
                "UnityEngine.GUIStyle",
                "UnityEngine.RectOffset",
                "UnityEngine.Color32",
                "UnityEngine.Matrix4x4",
                "UnityEngine.Rendering.SphericalHarmonicsL2",
                "UnityEngine.PropertyName",
                "UnityEngine.Vector2",
                "UnityEngine.Vector3",
                "UnityEngine.Vector4",
                "UnityEngine.Quaternion",
                "UnityEngine.Color",
                "UnityEngine.Rect",
                "UnityEngine.Bounds",
                "UnityEngine.LayerMask"
            };

            return unityTypes.Contains(type.FullName) ||
                   type.Namespace?.StartsWith("UnityEngine") == true &&
                   (type.IsValueType || Attribute.IsDefined(type, typeof(SerializableAttribute)));
        }

        private void ProcessUnityType(List<TypeTreeNode> nodes, Type type, string name, int indent)
        {
            switch (type.FullName)
            {
                case "UnityEngine.AnimationCurve":
                    Helper.AddAnimationCurve(nodes, name, indent);
                    break;
                case "UnityEngine.Gradient":
                    Helper.AddGradient(nodes, name, indent);
                    break;
                case "UnityEngine.GUIStyle":
                    Helper.AddGUIStyle(nodes, name, indent);
                    break;
                case "UnityEngine.RectOffset":
                    Helper.AddRectOffset(nodes, name, indent);
                    break;
                case "UnityEngine.Color32":
                    Helper.AddColor32(nodes, name, indent);
                    break;
                case "UnityEngine.Matrix4x4":
                    Helper.AddMatrix4x4(nodes, name, indent);
                    break;
                case "UnityEngine.Rendering.SphericalHarmonicsL2":
                    Helper.AddSphericalHarmonicsL2(nodes, name, indent);
                    break;
                case "UnityEngine.PropertyName":
                    Helper.AddPropertyName(nodes, name, indent);
                    break;
                default:
                    // Для других сериализуемых типов Unity создаем стандартные узлы
                    nodes.Add(new TypeTreeNode(type.Name, name, indent, false));

                    // Обрабатываем поля типа
                    var typeConverter = new TypeDefinitionConverter(type, Helper, indent + 1, genericTypeMap);
                    nodes.AddRange(typeConverter.ConvertToTypeTreeNodes());
                    break;
            }
        }

        private static bool ShouldImplementIDeserializable(Type type)
        {
            // Проверяем, реализует ли тип IDeserializable
            return type.GetInterfaces().Any(i => i.Name == "IDeserializable");
        }
    }

    public static class UnitySerializationLogic
    {
        public static bool WillUnitySerialize(FieldInfo field, Dictionary<string, Type> genericArguments = null)
        {
            // Пропускаем статические поля
            if (field.IsStatic)
                return false;

            // Пропускаем поля с атрибутом [NonSerialized]
            if (Attribute.IsDefined(field, typeof(NonSerializedAttribute)))
                return false;

            // Пропускаем private поля без [SerializeField]
            if (field.IsPrivate && !Attribute.IsDefined(field, typeof(SerializeField)))
                return false;

            // Пропускаем свойства (хотя FieldInfo не может быть свойством)

            // Проверяем тип поля
            var fieldType = field.FieldType;

            // Пропускаем типы с атрибутом [NonSerialized]
            if (Attribute.IsDefined(fieldType, typeof(NonSerializedAttribute)))
                return false;

            // Проверяем, является ли тип сериализуемым Unity
            if (IsSupportedType(fieldType))
                return true;

            // Для generic типов проверяем их аргументы
            if (fieldType.IsGenericType)
            {
                foreach (var arg in fieldType.GetGenericArguments())
                {
                    if (!IsSupportedType(arg))
                        return false;
                }
                return true;
            }

            return false;
        }

        public static bool IsNonSerialized(Type type)
        {
            return type == null ||
                   type == typeof(object) ||
                   Attribute.IsDefined(type, typeof(NonSerializedAttribute));
        }

        public static bool IsSupportedCollection(Type type)
        {
            if (type.IsArray)
                return true;

            if (type.IsGenericType)
            {
                var genericType = type.GetGenericTypeDefinition();
                return genericType == typeof(List<>) ||
                       genericType == typeof(Dictionary<,>) ||
                       genericType == typeof(HashSet<>);
            }

            return false;
        }

        private static bool IsSupportedType(Type type)
        {
            // Примитивные типы
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
                return true;

            // Unity типы
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return true;

            // Сериализуемые структуры
            if (type.IsValueType && Attribute.IsDefined(type, typeof(SerializableAttribute)))
                return true;

            // Классы с [Serializable]
            if (type.IsClass && Attribute.IsDefined(type, typeof(SerializableAttribute)))
                return true;

            return false;
        }
    }
}